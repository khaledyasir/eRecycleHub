using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

/// <summary>A stored in-app notification whose push has not been sent yet.</summary>
public sealed record PendingRow(int Id, int UserId, string Kind, decimal? Amount, int? AuctionId, string EventKey);

/// <summary>
/// The one push pipeline for stored notifications (auction events and wallet decisions). Pushes the rows with the given event keys that have not been
/// pushed yet: each row is claimed first (<c>PushedAtUtc</c>), so a repeated job sends nothing; each device gets its own language; dead tokens reported
/// by the push service are deleted; a push failure is logged and never undoes anything (the in-app rows are the source of truth).
/// </summary>
public sealed class NotificationPusher(AppDbContext db, IPushGateway push, TimeProvider clock, ILogger<NotificationPusher> log)
{
    /// <param name="auctionId">When set, only rows of that auction are considered.</param>
    /// <param name="build">Makes the message for one row and one device language ("en", "ar" or null for the default).</param>
    public async Task PushPendingAsync(IReadOnlyCollection<string> eventKeys, IReadOnlyCollection<int>? onlyUsers, int? auctionId,
        Func<PendingRow, string?, DateTime, PushMessage> build, CancellationToken ct)
    {
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => (auctionId == null || n.AuctionId == auctionId) && n.EventKey != null && eventKeys.Contains(n.EventKey) && n.PushedAtUtc == null
                && (onlyUsers == null || onlyUsers.Contains(n.UserId)))
            .Select(n => new PendingRow(n.Id, n.UserId, n.Kind, n.Amount, n.AuctionId, n.EventKey!)).ToListAsync(ct);
        if (rows.Count == 0) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = new List<PendingRow>();
        foreach (var r in rows)
            if (await db.Notifications.Where(n => n.Id == r.Id && n.PushedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.PushedAtUtc, now), ct) == 1)
                claimed.Add(r);
        if (claimed.Count == 0) return;

        var userIds = claimed.Select(c => c.UserId).Distinct().ToList();
        var tokens = await db.DeviceTokens.AsNoTracking().Where(d => userIds.Contains(d.UserId)).Select(d => new { d.UserId, d.Token, d.Language }).ToListAsync(ct);
        var targets = new List<PushTarget>();
        foreach (var c in claimed)
            foreach (var t in tokens.Where(t => t.UserId == c.UserId))
                targets.Add(new PushTarget(t.Token, build(c, t.Language, now)));
        if (targets.Count == 0) return;

        try
        {
            foreach (var batch in targets.Chunk(PushBatching.MaxTokensPerCall / 2))
            {
                var result = await push.SendAsync(batch, ct);
                if (result.InvalidTokens.Count > 0)
                {
                    var dead = result.InvalidTokens.ToList();
                    await db.DeviceTokens.Where(d => dead.Contains(d.Token)).ExecuteDeleteAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Push for {Rows} notification(s) failed; the in-app notifications are unaffected", claimed.Count);
        }
    }
}

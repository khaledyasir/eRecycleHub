using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

/// <summary>
/// Turns auction events into in-app notification rows (the source of truth, one per user and event: <c>Notification.EventKey</c> is unique per user)
/// and pushes to devices. Runs on the background queue after the database commit, so a push failure never breaks a bid or a close, and a repeated job
/// cannot notify twice: a row's push is claimed with <c>PushedAtUtc</c> before it is sent. Never moves money.
/// </summary>
public sealed class AuctionNotifier(AppDbContext db, IPushGateway push, NotificationPusher pusher, TimeProvider clock, ILogger<AuctionNotifier> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private sealed record Titles(string En, string Ar);

    private async Task<Titles> TitlesAsync(int auctionId, CancellationToken ct) =>
        await db.Auctions.AsNoTracking().Where(a => a.Id == auctionId).Select(a => new Titles(a.TitleEn, a.TitleAr)).FirstOrDefaultAsync(ct) ?? new Titles("", "");

    // ---------- a bid was placed ----------

    /// <summary>
    /// Everyone taking part in or watching the auction (except the bidder) gets a row and a push: the previous leader "outbid", the rest "new bid".
    /// FCM topics cannot leave a user out, so this goes to the exact devices. The bidder gets "joined" the first time only.
    /// </summary>
    public async Task NotifyBidPlacedAsync(long bidId, bool joined, int? previousLeader, CancellationToken ct = default)
    {
        var bid = await db.Bids.AsNoTracking().Where(b => b.Id == bidId).Select(b => new { b.AuctionId, b.UserId, b.Amount }).FirstOrDefaultAsync(ct);
        if (bid is null) return;
        var now = Now;
        var key = $"bid:{bidId}";

        var participants = await db.AuctionParticipants.AsNoTracking().Where(p => p.AuctionId == bid.AuctionId).Select(p => p.UserId).ToListAsync(ct);
        var watchers = await db.AuctionWatches.AsNoTracking().Where(w => w.AuctionId == bid.AuctionId).Select(w => w.UserId).ToListAsync(ct);
        var recipients = participants.Union(watchers).Where(u => u != bid.UserId).ToList();
        recipients = await db.Users.AsNoTracking().Where(u => recipients.Contains(u.Id) && u.IsActive && !u.IsDeleted).Select(u => u.Id).ToListAsync(ct);

        // Rows the bid transaction already wrote (outbid leader, watchers) are kept; the rest are added here. The unique key refuses duplicates.
        var existing = await db.Notifications.Where(n => n.EventKey == key && recipients.Contains(n.UserId)).Select(n => n.UserId).ToListAsync(ct);
        db.Notifications.AddRange(recipients.Except(existing).Select(u => new Notification
        {
            UserId = u, FromUserId = bid.UserId, Kind = u == previousLeader ? NotificationKinds.Outbid : NotificationKinds.NewBid, AuctionId = bid.AuctionId,
            Amount = bid.Amount, Url = $"/auctions/{bid.AuctionId}", CreatedAtUtc = now, EventKey = key,
        }));
        if (joined)
        {
            var joinKey = $"join:{bid.AuctionId}";
            if (!await db.Notifications.AnyAsync(n => n.UserId == bid.UserId && n.EventKey == joinKey, ct))
                db.Notifications.Add(new Notification { UserId = bid.UserId, Kind = NotificationKinds.Joined, AuctionId = bid.AuctionId, Url = $"/auctions/{bid.AuctionId}", CreatedAtUtc = now, EventKey = joinKey });
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // a concurrent run wrote the same event: its rows count

        await PushPendingAsync(bid.AuctionId, joined ? [key, $"join:{bid.AuctionId}"] : [key], null, null, ct);
    }

    // ---------- an auction ended ----------

    /// <summary>
    /// Everyone on the auction's topic gets one "ended" push (one text for all devices, both languages); the winner also gets their own push with the same
    /// collapse id, so the device shows one notification. <paramref name="charged"/> is what was taken from the winner's wallet (null when nothing was).
    /// </summary>
    public async Task NotifyAuctionEndedAsync(int auctionId, int winnerUserId, decimal? charged, CancellationToken ct = default)
    {
        var key = $"end:{auctionId}";
        var now = Now;
        var titles = await TitlesAsync(auctionId, ct);

        // Claim the others' rows: the topic push covers them. If nothing was left to claim, the topic push already went out.
        var claimed = await db.Notifications.Where(n => n.EventKey == key && n.UserId != winnerUserId && n.PushedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.PushedAtUtc, now), ct);
        if (claimed > 0)
        {
            try { await push.SendToTopicAsync(AuctionTopicService.TopicName(auctionId), PushPayloads.BuildEndedForTopic(auctionId, titles.En, titles.Ar, now), ct); }
            catch (Exception ex) { log.LogWarning(ex, "Topic push for the end of auction {AuctionId} failed", auctionId); }
        }
        await PushPendingAsync(auctionId, [key], [winnerUserId], charged is decimal c ? new Dictionary<int, decimal> { [winnerUserId] = c } : null, ct);
    }

    /// <summary>Tells the winner their wallet could not cover the winning amount.</summary>
    public Task NotifyPaymentFailedAsync(int auctionId, int winnerUserId, CancellationToken ct = default) =>
        PushPendingAsync(auctionId, [$"payfail:{auctionId}"], [winnerUserId], null, ct);

    // ---------- sending ----------

    /// <summary>Pushes the auction's rows with these event keys through the shared pipeline (<see cref="NotificationPusher"/>), with the auction's titles in the text.</summary>
    private async Task PushPendingAsync(int auctionId, IReadOnlyCollection<string> eventKeys, IReadOnlyCollection<int>? onlyUsers, IReadOnlyDictionary<int, decimal>? charged, CancellationToken ct)
    {
        var titles = await TitlesAsync(auctionId, ct);
        await pusher.PushPendingAsync(eventKeys, onlyUsers, auctionId,
            (r, language, now) => PushPayloads.Build(r.Kind, auctionId, r.Id, language, titles.En, titles.Ar, r.Amount,
                charged is not null && charged.TryGetValue(r.UserId, out var ch) ? ch : null, now), ct);
    }
}

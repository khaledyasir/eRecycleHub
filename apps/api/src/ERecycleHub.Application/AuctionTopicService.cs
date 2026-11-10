using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

/// <summary>
/// Auction participants and their push topics (<c>auction_{id}</c>, the <c>Auctions.TopicName</c> column). The database
/// (<c>AuctionParticipants</c> + <c>DeviceTokens</c>) is the source of truth; FCM subscriptions are derived from it and can always be
/// rebuilt (<see cref="SyncTokenAsync"/> on every token registration, <see cref="ResyncAllAsync"/> for everything).
/// Push calls never throw: a Firebase outage must not break a bid, a login or a logout.
/// </summary>
public sealed class AuctionTopicService(AppDbContext db, IPushGateway push, TimeProvider clock, ILogger<AuctionTopicService> log)
{
    public static string TopicName(int auctionId) => $"auction_{auctionId}";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Records that the user takes part in the auction. Idempotent and safe under concurrency (the key is range-locked for the check).
    /// Runs in the caller's transaction, so a bid and its participation commit together. Returns true only the first time.
    /// </summary>
    public async Task<bool> JoinAsync(int userId, int auctionId, CancellationToken ct = default)
    {
        var now = Now;
        var rows = await db.Database.ExecuteSqlAsync($@"INSERT INTO AuctionParticipants (AuctionId, UserId, JoinedAtUtc)
SELECT {auctionId}, {userId}, {now}
WHERE NOT EXISTS (SELECT 1 FROM AuctionParticipants WITH (UPDLOCK, HOLDLOCK) WHERE AuctionId = {auctionId} AND UserId = {userId})", ct);
        return rows == 1;
    }

    /// <summary>Topics of the auctions the user takes part in that have not ended.</summary>
    public async Task<List<string>> ActiveTopicsAsync(int userId, CancellationToken ct = default)
    {
        var now = Now;
        return await db.AuctionParticipants.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Join(db.Auctions, p => p.AuctionId, a => a.Id, (p, a) => a)
            .Where(a => a.IsActive && (a.EndsAtUtc == null || a.EndsAtUtc > now))
            .Select(a => a.TopicName!).ToListAsync(ct);
    }

    /// <summary>Subscribes every device of the user to the auction's topic.</summary>
    public async Task SubscribeUserAsync(int userId, int auctionId, CancellationToken ct = default)
    {
        var tokens = await TokensOfAsync(userId, ct);
        await ChangeAsync(tokens, TopicName(auctionId), subscribe: true, ct);
    }

    /// <summary>Unsubscribes every device of the user from the auction's topic.</summary>
    public async Task UnsubscribeUserAsync(int userId, int auctionId, CancellationToken ct = default)
    {
        var tokens = await TokensOfAsync(userId, ct);
        await ChangeAsync(tokens, TopicName(auctionId), subscribe: false, ct);
    }

    /// <summary>A new or moved device token joins the topics of every running auction its user takes part in.</summary>
    public async Task SyncTokenAsync(string token, int userId, CancellationToken ct = default)
    {
        foreach (var topic in await ActiveTopicsAsync(userId, ct))
            await ChangeAsync([token], topic, subscribe: true, ct);
    }

    /// <summary>Takes tokens off topics. Used after the tokens were deleted (logout, replaced device), so it needs no database state.</summary>
    public async Task UnsubscribeTokensAsync(IReadOnlyList<string> tokens, IReadOnlyList<string> topics, CancellationToken ct = default)
    {
        foreach (var topic in topics) await ChangeAsync(tokens, topic, subscribe: false, ct);
    }

    /// <summary>Rebuilds every subscription from the database: for each running auction, all devices of its participants. Returns the number of topics handled.</summary>
    public async Task<int> ResyncAllAsync(CancellationToken ct = default)
    {
        var now = Now;
        var auctions = await db.Auctions.AsNoTracking().Where(a => a.IsActive && (a.EndsAtUtc == null || a.EndsAtUtc > now))
            .Where(a => db.AuctionParticipants.Any(p => p.AuctionId == a.Id)).Select(a => new { a.Id, a.TopicName }).ToListAsync(ct);
        foreach (var a in auctions)
        {
            var tokens = await db.AuctionParticipants.AsNoTracking().Where(p => p.AuctionId == a.Id)
                .Join(db.DeviceTokens, p => p.UserId, d => d.UserId, (p, d) => d.Token).ToListAsync(ct);
            await ChangeAsync(tokens, a.TopicName ?? TopicName(a.Id), subscribe: true, ct);
        }
        return auctions.Count;
    }

    private Task<List<string>> TokensOfAsync(int userId, CancellationToken ct) =>
        db.DeviceTokens.AsNoTracking().Where(d => d.UserId == userId).Select(d => d.Token).ToListAsync(ct);

    /// <summary>One topic change for any number of tokens, in batches. Dead tokens are deleted; failures are logged and swallowed.</summary>
    private async Task ChangeAsync(IReadOnlyList<string> tokens, string topic, bool subscribe, CancellationToken ct)
    {
        foreach (var batch in PushBatching.Split(tokens))
        {
            try
            {
                var result = subscribe ? await push.SubscribeAsync(batch, topic, ct) : await push.UnsubscribeAsync(batch, topic, ct);
                if (result.InvalidTokens.Count > 0)
                {
                    var dead = result.InvalidTokens.ToList();
                    var removed = await db.DeviceTokens.Where(d => dead.Contains(d.Token)).ExecuteDeleteAsync(ct);
                    log.LogInformation("Removed {Count} invalid push token(s) reported by the push service", removed);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Push {Action} of {Count} token(s) on {Topic} failed", subscribe ? "subscribe" : "unsubscribe", batch.Length, topic);
            }
        }
    }
}

using System.Collections.Concurrent;
using ERecycleHub.Application;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api;

/// <summary>
/// Every few seconds, finds auctions that need closing and queues one <see cref="AuctionClosing"/> job per auction on the shared <see cref="BackgroundWorkQueue"/>
/// (the queue runner does the work). It selects: ended, active auctions whose winner holds money that is not paid yet (including ones whose payment failed:
/// they are retried), and ended auctions with bids whose winner was not notified yet, limited to those that ended within <c>AuctionEnd:LookbackHours</c> so
/// that switching this on does not announce old auctions. Closing is idempotent, so a duplicate or a restart does no harm.
/// Settings: <c>AuctionEnd:Enabled</c> (true), <c>AuctionEnd:PollSeconds</c> (15), <c>AuctionEnd:LookbackHours</c> (6).
/// </summary>
public sealed class AuctionEndScanner(IServiceScopeFactory scopes, BackgroundWorkQueue queue, TimeProvider clock, IConfiguration config, ILogger<AuctionEndScanner> log) : BackgroundService
{
    private const int BatchSize = 50;
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("AuctionEnd:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("AuctionEnd:PollSeconds", 15), 1, 300)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Auction end scan failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddHours(-Math.Clamp(config.GetValue("AuctionEnd:LookbackHours", 6), 1, 24 * 14));
        var ids = await db.Auctions.AsNoTracking()
            .Where(a => a.IsActive && a.EndsAtUtc != null && a.EndsAtUtc <= now
                && ((a.EndProcessedAtUtc == null && a.BidCount > 0 && a.EndsAtUtc >= since)
                    || db.Bids.Any(b => b.AuctionId == a.Id && b.HeldAmount > 0 && b.ReleasedAtUtc == null && b.SettledAtUtc == null)))
            .OrderBy(a => a.EndsAtUtc).Select(a => a.Id).Take(BatchSize).ToListAsync(ct);
        foreach (var id in ids)
        {
            if (!_inFlight.TryAdd(id, 0)) continue;
            var queued = queue.TryEnqueue($"auction.close:{id}", async (sp, c) =>
            {
                try { await sp.GetRequiredService<AuctionClosing>().CloseAsync(id, c); }
                finally { _inFlight.TryRemove(id, out _); }
            });
            if (!queued) _inFlight.TryRemove(id, out _);
        }
    }
}

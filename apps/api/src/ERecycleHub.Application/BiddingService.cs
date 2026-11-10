using System.Data;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.Application;

/// <param name="Held">What the bidder's bid now reserves (0 when wallet holds are off).</param>
/// <param name="ReleasedFromPrevious">What the previous leader's released bid had reserved (0 when it held nothing).</param>
/// <param name="AvailableAfter">What the bidder can still spend: balance minus pending withdrawals minus all their holds.</param>
public sealed record BidPlaced(long BidId, int AuctionId, decimal CurrentPrice, decimal MinimumNextBid, int BidCount, int UserId = 0, DateTime CreatedAtUtc = default,
    decimal Held = 0, decimal ReleasedFromPrevious = 0, decimal? AvailableAfter = null);

/// <summary>
/// Bids and the wallet money they reserve. A bid only HOLDS money (<see cref="Bid.HeldAmount"/>): the wallet balance and ledger are untouched by a
/// bid, a raise, being outbid or a deactivation. Money moves once, when the auction ends and the winner's hold becomes a payment
/// (<see cref="AuctionClosing"/>). Only the current leader holds; a user may lead several auctions while the sum of their holds fits their balance
/// minus pending withdrawals.
/// LOCK ORDER (everywhere that touches both): auction rows first (ascending id), then wallet rows (ascending user id), then plain inserts/updates.
/// Nothing may take a wallet lock and then an auction lock.
/// </summary>
public sealed class BiddingService(AppDbContext db, TimeProvider clock, WalletService wallet, AuctionTopicService topics, BackgroundWorkQueue queue, IBiddingSettings settings)
{
    private const int DeadlockNumber = 1205;

    /// <summary>
    /// Places a bid atomically: the bid, the price, the participant row, the new hold and the release of the previous one commit together or not at
    /// all. A deadlock victim is retried once (nothing has side effects before the commit).
    /// </summary>
    public async Task<BidPlaced> PlaceBidAsync(int userId, int auctionId, decimal amount, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount)) throw new AppException(BidErrors.InvalidAmount);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (placed, joined, previousLeader) = await TryPlaceBidAsync(userId, auctionId, amount, ct);
                // After the commit: a push failure or a lost queue item can never undo or fail the bid (the next token sync rebuilds subscriptions).
                if (joined) queue.TryEnqueue("push.subscribe-participant", (sp, c) => sp.GetRequiredService<AuctionTopicService>().SubscribeUserAsync(userId, auctionId, c));
                queue.TryEnqueue("notify.bid-placed", (sp, c) => sp.GetRequiredService<AuctionNotifier>().NotifyBidPlacedAsync(placed.BidId, joined, previousLeader, c));
                return placed;
            }
            catch (Exception ex) when (attempt == 1 && IsDeadlock(ex) && !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<(BidPlaced, bool joined, int? previousLeader)> TryPlaceBidAsync(int userId, int auctionId, decimal amount, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1. The auction row serialises every bid on this auction.
        if (!await LockAuctionAsync(auctionId, ct)) throw new AppException(BidErrors.NotFound, 404);

        var a = await db.Auctions.AsNoTracking().Where(x => x.Id == auctionId).Select(x => new
        {
            x.IsActive,
            x.StartsAtUtc,
            x.EndsAtUtc,
            x.CurrentPrice,
            x.BidIncrement,
            x.InsuranceAmount,
            x.TitleEn,
            OwnerUserId = x.Publisher == null ? (int?)null : x.Publisher.UserId,
            PublisherActive = x.Publisher == null || x.Publisher.User!.IsActive,
        }).SingleAsync(ct);

        // The current leader: told they were outbid, and their hold (if any) is released. Read before this bid exists.
        var top = await TopBidAsync(auctionId, ct);
        var topHeld = top is { ReleasedAtUtc: null, SettledAtUtc: null } ? top.HeldAmount : 0m;
        var ownHold = top is not null && top.UserId == userId ? topHeld : 0m;

        // 2. The bidder's wallet row: every bid and withdrawal request of this user is serialised here, so two bids in two auctions cannot over-commit.
        await LockWalletAsync(userId, ct);

        var bidder = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Role, Active = u.IsActive && !u.IsDeleted, u.WalletBalance }).SingleOrDefaultAsync(ct) ?? throw new AppException(BidErrors.AccountBlocked, 403);
        var pending = await wallet.PendingWithdrawalsAsync(userId, ct);   // stable: withdrawal requests lock this wallet row too
        var held = await wallet.HeldAsync(userId, ct);                    // includes ownHold
        var holdsOn = settings.DebitWallet;
        // What this bid may reserve: the free money plus what this user's own bid in THIS auction already reserves (it moves to the new bid).
        var availableForThis = Math.Max(0m, bidder.WalletBalance - pending - held) + ownHold;

        var now = clock.GetUtcNow().UtcDateTime;
        var context = new AuctionRules.BidContext(a.IsActive, a.PublisherActive, a.StartsAtUtc, a.EndsAtUtc, a.CurrentPrice, a.BidIncrement,
            a.InsuranceAmount, a.OwnerUserId, userId, bidder.Role, bidder.Active, holdsOn ? availableForThis : bidder.WalletBalance);
        var error = AuctionRules.Validate(context, amount, now);
        if (error is not null)
        {
            object? args = error switch
            {
                BidErrors.BelowMinimum => new { minimum = AuctionRules.MinimumNextBid(a.CurrentPrice, a.BidIncrement), currentPrice = a.CurrentPrice },
                BidErrors.InsufficientInsurance => new { insurance = a.InsuranceAmount },
                _ => null,
            };
            throw new AppException(error, 400, args);
        }
        if (holdsOn && amount > availableForThis) throw new AppException(BidErrors.InsufficientBalance, 422, new { required = amount, available = availableForThis });

        // Flag off: no new hold, but a leader's existing hold travels with their raised bid so it is still released/charged later.
        var newHeld = holdsOn ? amount : ownHold;

        var bid = new Bid { AuctionId = auctionId, UserId = userId, Amount = amount, HeldAmount = newHeld, CreatedAtUtc = now };
        db.Bids.Add(bid);
        await db.Auctions.Where(x => x.Id == auctionId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CurrentPrice, amount)
            .SetProperty(x => x.BidCount, x => x.BidCount + 1)
            .SetProperty(x => x.UpdatedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);   // the bid id is needed for the notification keys

        // Outbid, or raising one's own bid: the old bid's hold ends (for a raise it moved to the new bid). Nothing is credited: nothing was debited.
        if (topHeld > 0 && top is not null) await ReleaseBidAsync(top.Id, now, ct);
        var joined = await topics.JoinAsync(userId, auctionId, ct);

        // In-app notifications (legacy behaviour, kept in this transaction): the outbid leader, and everyone watching the auction except the bidder.
        var previousLeader = top is not null && top.UserId != userId ? top.UserId : (int?)null;
        var eventKey = $"bid:{bid.Id}";
        if (previousLeader is int outbid)
            db.Notifications.Add(new Notification
            {
                UserId = outbid, FromUserId = userId, Kind = NotificationKinds.Outbid, AuctionId = auctionId, Amount = amount,
                Url = $"/auctions/{auctionId}", CreatedAtUtc = now, EventKey = eventKey,
            });
        var watchers = await db.AuctionWatches.Where(w => w.AuctionId == auctionId && w.UserId != userId && w.UserId != previousLeader)
            .Select(w => w.UserId).ToListAsync(ct);
        db.Notifications.AddRange(watchers.Select(w => new Notification
        {
            UserId = w, FromUserId = userId, Kind = NotificationKinds.NewBid, AuctionId = auctionId, Amount = amount, Url = $"/auctions/{auctionId}", CreatedAtUtc = now, EventKey = eventKey,
        }));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var count = await db.Auctions.Where(x => x.Id == auctionId).Select(x => x.BidCount).SingleAsync(ct);
        var placed = new BidPlaced(bid.Id, auctionId, amount, AuctionRules.MinimumNextBid(amount, a.BidIncrement), count, userId, now,
            newHeld, top is not null && top.UserId != userId ? topHeld : 0m, holdsOn ? availableForThis - newHeld : Math.Max(0m, bidder.WalletBalance - pending - held));
        return (placed, joined, previousLeader);
    }

    // ---------- holds released or restored when an auction (or its seller) is deactivated or reactivated ----------

    /// <summary>
    /// Locks the auctions of the seller account in ascending id order and returns their ids. Call it FIRST in any transaction that is about to change
    /// the seller's user row, so the lock order stays auction → wallet. Empty for accounts that are not sellers.
    /// </summary>
    public async Task<List<int>> LockSellerAuctionsAsync(int userId, CancellationToken ct = default)
    {
        RequireTransaction();
        var ids = await db.Auctions.AsNoTracking().Where(a => a.Publisher != null && a.Publisher.UserId == userId).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync(ct);
        foreach (var id in ids) await LockAuctionAsync(id, ct);
        return ids;
    }

    /// <summary>
    /// Releases the leader's hold on each auction (an auction or its seller was deactivated). No ledger entry: nothing was debited. An already paid
    /// (settled) bid is left alone: refunding a payment would be an explicit administrator credit. Runs in the caller's transaction, auctions locked
    /// first (ascending). Needs no wallet lock: releasing a hold only makes money available.
    /// </summary>
    public async Task<int> ReleaseHoldsAsync(IReadOnlyList<int> auctionIds, int? actorUserId, CancellationToken ct = default)
    {
        RequireTransaction();
        var ids = auctionIds.Distinct().Order().ToList();
        foreach (var id in ids) await LockAuctionAsync(id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var released = 0;
        foreach (var id in ids)
            if (await TopBidAsync(id, ct) is { ReleasedAtUtc: null, SettledAtUtc: null, HeldAmount: > 0 } top)
            {
                await ReleaseBidAsync(top.Id, now, ct);
                released++;
            }
        return released;
    }

    /// <summary>
    /// The reverse of <see cref="ReleaseHoldsAsync"/> for auctions that are active again (and whose seller is): reserves the leader's bid again. If the
    /// leader's available balance no longer covers it the whole change is refused (409 <c>auction.leader_unfunded</c>), so an unfunded bid can never win.
    /// </summary>
    public async Task<int> RestoreHoldsAsync(IReadOnlyList<int> auctionIds, int? actorUserId, CancellationToken ct = default)
    {
        RequireTransaction();
        var ids = auctionIds.Distinct().Order().ToList();
        foreach (var id in ids) await LockAuctionAsync(id, ct);

        var due = new List<(int AuctionId, TopBid Bid)>();
        foreach (var id in ids)
        {
            var live = await db.Auctions.AsNoTracking().AnyAsync(a => a.Id == id && a.IsActive && (a.Publisher == null || a.Publisher.User!.IsActive), ct);
            if (live && await TopBidAsync(id, ct) is { ReleasedAtUtc: not null, SettledAtUtc: null, HeldAmount: > 0 } top) due.Add((id, top));
        }
        foreach (var userId in due.Select(d => d.Bid.UserId).Distinct().Order()) await LockWalletAsync(userId, ct);
        foreach (var (auctionId, bid) in due)
        {
            var b = await wallet.BalanceAsync(bid.UserId, ct);   // the released hold no longer counts, so Available is what is free now
            if (b.Available < bid.HeldAmount)
                throw AppException.Conflict("auction.leader_unfunded", new { auctionId, required = bid.HeldAmount, available = b.Available });
            await db.Bids.Where(x => x.Id == bid.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAtUtc, (DateTime?)null), ct);
        }
        return due.Count;
    }

    // ---------- helpers ----------

    private sealed record TopBid(long Id, int UserId, decimal Amount, decimal HeldAmount, DateTime? ReleasedAtUtc, DateTime? SettledAtUtc);

    private Task<TopBid?> TopBidAsync(int auctionId, CancellationToken ct) =>
        db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
            .Select(b => new TopBid(b.Id, b.UserId, b.Amount, b.HeldAmount, b.ReleasedAtUtc, b.SettledAtUtc)).FirstOrDefaultAsync(ct);

    private Task ReleaseBidAsync(long bidId, DateTime now, CancellationToken ct) =>
        db.Bids.Where(b => b.Id == bidId && b.ReleasedAtUtc == null && b.SettledAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(b => b.ReleasedAtUtc, now), ct);

    private async Task<bool> LockAuctionAsync(int id, CancellationToken ct) =>
        (await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Auctions WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}").ToListAsync(ct)).Count == 1;

    private Task LockWalletAsync(int userId, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}").ToListAsync(ct);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Releasing or restoring bid holds must run inside the caller's database transaction.");
    }

    private static bool IsDeadlock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
            if (e is Microsoft.Data.SqlClient.SqlException { Number: DeadlockNumber }) return true;
        return false;
    }
}

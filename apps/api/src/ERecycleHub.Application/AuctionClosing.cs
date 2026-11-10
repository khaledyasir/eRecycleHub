using System.Data;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

public enum CloseOutcome
{
    /// <summary>Nothing to do: not ended, deactivated, no bids, already done, or the sale was voided.</summary>
    Skipped,
    /// <summary>The winner's hold became a payment (an AuctionPayment ledger entry) and the winner was notified.</summary>
    Settled,
    /// <summary>The winner is notified; nothing was charged because the winning bid held no money (placed before wallet holds, or with holds off).</summary>
    NoHold,
    /// <summary>The winner's balance did not cover the winning amount. Nothing changed except the failure marker; the job retries.</summary>
    PaymentFailed,
}

public sealed record CloseResult(CloseOutcome Outcome, int? WinnerUserId = null, decimal Charged = 0);

/// <summary>
/// Closes an ended auction exactly once: turns the winner's hold into a payment and tells the participants. The ONLY code that moves money for a bid.
/// One transaction, auction row locked first, then the winner's wallet row. Idempotent: the bid is settled with a conditional UPDATE, a filtered unique
/// index on the ledger refuses a second payment, and the end processing (in-app rows) is claimed through <c>Auctions.EndProcessedAtUtc</c>. <c>WinnerNotifiedAtUtc</c> and the admin winner e-mail are not touched.
/// The money goes to the platform: there is no seller payout and no platform wallet, the ledger entry on the winner is the record.
/// </summary>
public sealed class AuctionClosing(AppDbContext db, TimeProvider clock, WalletService wallet, BiddingService bidding, BackgroundWorkQueue queue, Audit audit,
    ILogger<AuctionClosing> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CloseResult> CloseAsync(int auctionId, CancellationToken ct = default)
    {
        CloseResult result;
        bool notified;
        try
        {
            (result, notified) = await TryCloseAsync(auctionId, ct);
        }
        catch (AppException ex) when (ex.Code == "wallet.insufficient")
        {
            return await RecordPaymentFailureAsync(auctionId, ct);
        }

        // After the commit: pushes are best effort and run on the queue; the in-app rows are the source of truth.
        if (result.Outcome is CloseOutcome.Settled or CloseOutcome.NoHold && notified && result.WinnerUserId is int winner)
        {
            var charged = result.Outcome == CloseOutcome.Settled ? result.Charged : (decimal?)null;
            queue.TryEnqueue($"notify.auction-ended:{auctionId}", (sp, c) => sp.GetRequiredService<AuctionNotifier>().NotifyAuctionEndedAsync(auctionId, winner, charged, c));
        }
        return result;
    }

    private async Task<(CloseResult, bool notified)> TryCloseAsync(int auctionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1. The auction row first (the same lock bids take), then re-check everything under it: an admin may have changed the auction since it was selected.
        if ((await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Auctions WITH (UPDLOCK, ROWLOCK) WHERE Id = {auctionId}").ToListAsync(ct)).Count == 0)
            return (new CloseResult(CloseOutcome.Skipped), false);
        var now = Now;
        var a = await db.Auctions.AsNoTracking().Where(x => x.Id == auctionId).Select(x => new
        {
            x.IsActive, x.EndsAtUtc, x.SettlementFailedAtUtc,
            PublisherActive = x.Publisher == null || x.Publisher.User!.IsActive,
        }).SingleAsync(ct);
        if (!a.IsActive || !a.PublisherActive || a.EndsAtUtc is null || a.EndsAtUtc > now) return (new CloseResult(CloseOutcome.Skipped), false);

        var top = await db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
            .Select(b => new { b.Id, b.UserId, b.Amount, b.HeldAmount, b.ReleasedAtUtc, b.SettledAtUtc }).FirstOrDefaultAsync(ct);
        if (top is null) return (new CloseResult(CloseOutcome.Skipped), false);
        if (top.HeldAmount > 0 && top.ReleasedAtUtc is not null && top.SettledAtUtc is null) return (new CloseResult(CloseOutcome.Skipped), false);   // sale voided or deactivated
        if (top.SettledAtUtc is not null) return (new CloseResult(CloseOutcome.Skipped), false);                                                         // already paid and announced by an earlier run

        // 2. Settle: the hold becomes a payment, in this same transaction. Needs the winner's wallet row, locked after the auction row.
        var charged = 0m;
        if (top is { HeldAmount: > 0, ReleasedAtUtc: null, SettledAtUtc: null })
        {
            await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {top.UserId}").ToListAsync(ct);
            var settled = await db.Bids.Where(b => b.Id == top.Id && b.SettledAtUtc == null && b.ReleasedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.SettledAtUtc, now), ct);
            if (settled == 0) return (new CloseResult(CloseOutcome.Skipped), false);
            // Throws wallet.insufficient (422) when the balance does not cover it; the whole transaction then rolls back.
            await wallet.DebitAsync(top.UserId, top.HeldAmount, LedgerReason.AuctionPayment, LedgerReferences.Bid, top.Id, null, ct, DebitGuard.ConsumeHold);
            charged = top.HeldAmount;
        }

        // 3. Tell the participants once: claim EndProcessedAtUtc, then write the in-app rows (a unique event key backs the claim).
        var claimed = await db.Auctions.Where(x => x.Id == auctionId && x.EndProcessedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndProcessedAtUtc, now), ct) == 1;
        if (claimed)
        {
            var key = $"end:{auctionId}";
            var others = await db.AuctionParticipants.AsNoTracking().Where(p => p.AuctionId == auctionId && p.UserId != top.UserId && db.Users.Any(u => u.Id == p.UserId && u.IsActive && !u.IsDeleted))
                .Select(p => p.UserId).ToListAsync(ct);
            db.Notifications.Add(new Notification { UserId = top.UserId, Kind = NotificationKinds.AuctionWon, AuctionId = auctionId, Amount = top.Amount, Url = $"/auctions/{auctionId}", CreatedAtUtc = now, EventKey = key });
            db.Notifications.AddRange(others.Select(u => new Notification { UserId = u, Kind = NotificationKinds.Ended, AuctionId = auctionId, Url = $"/auctions/{auctionId}", CreatedAtUtc = now, EventKey = key }));
        }
        if (a.SettlementFailedAtUtc is not null && charged > 0)
            await db.Auctions.Where(x => x.Id == auctionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SettlementFailedAtUtc, (DateTime?)null), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (new CloseResult(charged > 0 ? CloseOutcome.Settled : CloseOutcome.NoHold, top.UserId, charged), claimed);
    }

    /// <summary>
    /// The winner cannot pay: the settlement transaction was rolled back, so nothing moved. Marks the auction once (<c>SettlementFailedAtUtc</c>), tells the winner
    /// to top up, and leaves the hold in place; the close job retries every tick, so topping up settles it by itself. Administrators see it in the unsettled list.
    /// </summary>
    private async Task<CloseResult> RecordPaymentFailureAsync(int auctionId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Auctions WITH (UPDLOCK, ROWLOCK) WHERE Id = {auctionId}").ToListAsync(ct);
        var top = await db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
            .Select(b => new { b.UserId, b.Amount, b.HeldAmount, b.ReleasedAtUtc, b.SettledAtUtc }).FirstOrDefaultAsync(ct);
        if (top is null or { ReleasedAtUtc: not null } or { SettledAtUtc: not null }) return new CloseResult(CloseOutcome.Skipped);

        var now = Now;
        var first = await db.Auctions.Where(x => x.Id == auctionId && x.SettlementFailedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.SettlementFailedAtUtc, now), ct) == 1;
        if (first)
        {
            db.Notifications.Add(new Notification
            {
                UserId = top.UserId, Kind = NotificationKinds.PaymentFailed, AuctionId = auctionId, Amount = top.HeldAmount, Url = $"/auctions/{auctionId}", CreatedAtUtc = now, EventKey = $"payfail:{auctionId}",
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (first)
        {
            log.LogError("Auction {AuctionId}: the winner's wallet cannot cover the winning amount {Amount}. Nothing was charged; it is retried every tick and listed for administrators.", auctionId, top.HeldAmount);
            queue.TryEnqueue($"notify.payment-failed:{auctionId}", (sp, c) => sp.GetRequiredService<AuctionNotifier>().NotifyPaymentFailedAsync(auctionId, top.UserId, c));
        }
        return new CloseResult(CloseOutcome.PaymentFailed, top.UserId, 0);
    }

    /// <summary>
    /// Administrator action for a sale that cannot be completed (the winner cannot pay): releases the winner's hold so the auction ends without a payment. Auction locked first.
    /// </summary>
    public async Task VoidSaleAsync(Actor admin, int auctionId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var released = await bidding.ReleaseHoldsAsync([auctionId], admin.UserId, ct);
        if (released == 0)
        {
            if (!await db.Auctions.AnyAsync(a => a.Id == auctionId, ct)) throw AppException.NotFound("auction");
            throw AppException.Conflict("auction.no_unsettled_sale");
        }
        audit.Record(admin, "auction.void_sale", nameof(Auction), auctionId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

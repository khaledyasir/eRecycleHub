using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record UserBalanceMismatch(int UserId, decimal WalletBalance, decimal LedgerSum, decimal Difference);

/// <summary>A user whose balance cannot cover what is reserved: pending withdrawals plus held bids exceed the balance.</summary>
public sealed record OverCommittedUser(int UserId, decimal Balance, decimal Pending, decimal Held, decimal Shortfall);

public sealed record AuctionHoldMismatch(int AuctionId, decimal HeldOnBids, int HoldingBids, string Problem);

public sealed record SettlementMismatch(long BidId, int AuctionId, string Problem);

/// <summary>An ended auction whose winner has not been charged yet. <see cref="Shortfall"/> is how much more the winner's balance needs (0 when it is just waiting for the next job run).</summary>
public sealed record UnsettledAuction(int AuctionId, string RefNumber, string TitleEn, string TitleAr, DateTime? EndedAtUtc, DateTime? SettlementFailedAtUtc,
    int WinnerUserId, string WinnerName, string WinnerEmail, decimal Amount, decimal WinnerBalance, decimal Shortfall);

public sealed record WalletConsistencyReport(
    int UsersChecked, decimal TotalBalances, decimal TotalLedger, IReadOnlyList<UserBalanceMismatch> UserMismatches, IReadOnlyList<OverCommittedUser> OverCommitted,
    int AuctionsWithHolds, decimal TotalHeld, IReadOnlyList<AuctionHoldMismatch> AuctionMismatches,
    int PaymentsChecked, decimal PlatformRevenue, IReadOnlyList<SettlementMismatch> SettlementMismatches,
    IReadOnlyList<UnsettledAuction> UnsettledWarnings, int LegacyHoldLedgerRows, bool Truncated)
{
    /// <summary>No errors. Warnings (unsettled auctions, leftover old-model ledger rows) do not make a report fail.</summary>
    public bool Ok => UserMismatches.Count == 0 && OverCommitted.Count == 0 && AuctionMismatches.Count == 0 && SettlementMismatches.Count == 0 && TotalBalances == TotalLedger;
}

/// <summary>
/// Read-only check that the wallet books add up in the hold-only model. Holds never touch the ledger, so: per user, the balance equals the sum of that user's ledger
/// entries and balance − pending withdrawals − held bids is not negative; per auction, at most one bid holds and it is the top bid; every paid bid (settled)
/// has exactly one AuctionPayment entry for its amount and every such entry belongs to a settled bid. Warnings: ended auctions still unpaid (waiting, or payment failed)
/// and leftover ledger rows of the old debit-at-bid model. Writes nothing.
/// </summary>
public sealed class WalletConsistency(AppDbContext db, TimeProvider clock)
{
    private const int MaxListed = 200;
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
    private static readonly LedgerReason[] OldModel = [LedgerReason.BidDeduction, LedgerReason.BidRefund, LedgerReason.AuctionRefund, LedgerReason.AuctionRehold];

    public async Task<WalletConsistencyReport> CheckAsync(CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.WalletBalance, Ledger = db.WalletLedger.Where(l => l.UserId == u.Id).Sum(l => (decimal?)l.Amount) ?? 0m })
            .ToListAsync(ct);
        var userMismatches = users.Where(u => u.WalletBalance != u.Ledger)
            .Select(u => new UserBalanceMismatch(u.Id, u.WalletBalance, u.Ledger, u.WalletBalance - u.Ledger)).ToList();

        // Active holds: not released, not yet paid.
        var holdsByUser = await db.Bids.AsNoTracking().Where(b => b.HeldAmount > 0 && b.ReleasedAtUtc == null && b.SettledAtUtc == null)
            .GroupBy(b => b.UserId).Select(g => new { UserId = g.Key, Held = g.Sum(b => b.HeldAmount) }).ToDictionaryAsync(x => x.UserId, x => x.Held, ct);
        var pendingByUser = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.Type == WalletTransactionType.Withdrawal && t.Status == WalletTransactionStatus.Pending)
            .GroupBy(t => t.UserId).Select(g => new { UserId = g.Key, Pending = g.Sum(t => t.Amount) }).ToDictionaryAsync(x => x.UserId, x => x.Pending, ct);
        var overCommitted = users
            .Where(u => holdsByUser.ContainsKey(u.Id) || pendingByUser.ContainsKey(u.Id))
            .Select(u => new OverCommittedUser(u.Id, u.WalletBalance, pendingByUser.GetValueOrDefault(u.Id), holdsByUser.GetValueOrDefault(u.Id),
                pendingByUser.GetValueOrDefault(u.Id) + holdsByUser.GetValueOrDefault(u.Id) - u.WalletBalance))
            .Where(x => x.Shortfall > 0).ToList();

        var holdsByAuction = await db.Bids.AsNoTracking().Where(b => b.HeldAmount > 0 && b.ReleasedAtUtc == null && b.SettledAtUtc == null)
            .GroupBy(b => b.AuctionId).Select(g => new { AuctionId = g.Key, Held = g.Sum(b => b.HeldAmount), Count = g.Count() }).ToListAsync(ct);
        var auctionMismatches = new List<AuctionHoldMismatch>();
        foreach (var h in holdsByAuction)
        {
            string? problem = null;
            if (h.Count > 1) problem = "more than one bid holds money";
            else
            {
                var top = await db.Bids.AsNoTracking().Where(b => b.AuctionId == h.AuctionId).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
                    .Select(b => new { b.ReleasedAtUtc, b.SettledAtUtc, b.HeldAmount }).FirstAsync(ct);
                if (top.ReleasedAtUtc is not null || top.SettledAtUtc is not null || top.HeldAmount == 0) problem = "the holding bid is not the top bid";
            }
            if (problem is not null) auctionMismatches.Add(new AuctionHoldMismatch(h.AuctionId, h.Held, h.Count, problem));
        }

        // Payments: one AuctionPayment per settled bid, for its amount. Bids settled by the old-model conversion were charged at bid time and have no such entry.
        var payments = await db.WalletLedger.AsNoTracking().Where(l => l.Reason == LedgerReason.AuctionPayment).Select(l => new { l.UserId, l.Amount, l.ReferenceId }).ToListAsync(ct);
        var settled = await db.Bids.AsNoTracking().Where(b => b.SettledAtUtc != null && b.HeldAmount > 0).Select(b => new { b.Id, b.AuctionId, b.UserId, b.HeldAmount }).ToListAsync(ct);
        var oldModelUsers = (await db.WalletLedger.AsNoTracking().Where(l => OldModel.Contains(l.Reason) || l.Reason == LedgerReason.HoldConversion).Select(l => l.UserId).Distinct().ToListAsync(ct)).ToHashSet();
        var settlementMismatches = new List<SettlementMismatch>();
        var settledIds = settled.Select(s => (long)s.Id).ToHashSet();
        foreach (var s in settled)
        {
            var mine = payments.Where(p => p.ReferenceId == s.Id).ToList();
            if (mine.Count == 0 && !oldModelUsers.Contains(s.UserId)) settlementMismatches.Add(new SettlementMismatch(s.Id, s.AuctionId, "settled bid has no AuctionPayment ledger entry"));
            else if (mine.Count > 1) settlementMismatches.Add(new SettlementMismatch(s.Id, s.AuctionId, "settled bid was charged more than once"));
            else if (mine.Count == 1 && mine[0].Amount != -s.HeldAmount) settlementMismatches.Add(new SettlementMismatch(s.Id, s.AuctionId, $"payment {-mine[0].Amount:0.00} differs from the held amount {s.HeldAmount:0.00}"));
        }
        foreach (var p in payments.Where(p => p.ReferenceId is null || !settledIds.Contains(p.ReferenceId.Value)))
            settlementMismatches.Add(new SettlementMismatch(p.ReferenceId ?? 0, 0, "AuctionPayment entry does not belong to a settled bid"));

        var unsettled = await UnsettledAsync(failedOnly: false, ct);
        var legacy = await db.WalletLedger.AsNoTracking().CountAsync(l => OldModel.Contains(l.Reason), ct);

        return new WalletConsistencyReport(
            users.Count, users.Sum(u => u.WalletBalance), users.Sum(u => u.Ledger), userMismatches.Take(MaxListed).ToList(), overCommitted.Take(MaxListed).ToList(),
            holdsByAuction.Count, holdsByAuction.Sum(h => h.Held), auctionMismatches.Take(MaxListed).ToList(),
            payments.Count, -payments.Sum(p => p.Amount), settlementMismatches.Take(MaxListed).ToList(),
            unsettled, legacy,
            userMismatches.Count > MaxListed || overCommitted.Count > MaxListed || auctionMismatches.Count > MaxListed || settlementMismatches.Count > MaxListed);
    }

    /// <summary>
    /// Ended, active auctions whose winner still holds money that has not been charged. With <paramref name="failedOnly"/>: just those whose payment failed at close
    /// (<c>SettlementFailedAtUtc</c>), the administrators' list. Without it: also auctions that ended a couple of minutes ago and are still waiting (a warning).
    /// </summary>
    public async Task<List<UnsettledAuction>> UnsettledAsync(bool failedOnly, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = now - Grace;
        var rows = await db.Auctions.AsNoTracking()
            .Where(a => a.IsActive && a.EndsAtUtc != null && a.EndsAtUtc <= now)
            .Where(a => failedOnly ? a.SettlementFailedAtUtc != null : (a.SettlementFailedAtUtc != null || a.EndsAtUtc <= stale))
            .Select(a => new
            {
                a.Id, a.RefNumber, a.TitleEn, a.TitleAr, a.EndsAtUtc, a.SettlementFailedAtUtc,
                Top = db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
                    .Select(b => new { b.UserId, b.HeldAmount, b.ReleasedAtUtc, b.SettledAtUtc, b.User!.FullName, b.User.Email, b.User.WalletBalance }).FirstOrDefault(),
            })
            .Where(x => x.Top != null && x.Top.HeldAmount > 0 && x.Top.ReleasedAtUtc == null && x.Top.SettledAtUtc == null)
            .OrderBy(x => x.EndsAtUtc).Take(MaxListed).ToListAsync(ct);
        return rows.Select(x => new UnsettledAuction(x.Id, x.RefNumber, x.TitleEn, x.TitleAr, x.EndsAtUtc, x.SettlementFailedAtUtc, x.Top!.UserId, x.Top.FullName, x.Top.Email,
            x.Top.HeldAmount, x.Top.WalletBalance, Math.Max(0m, x.Top.HeldAmount - x.Top.WalletBalance))).ToList();
    }
}

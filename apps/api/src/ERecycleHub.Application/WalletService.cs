using System.Data;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record WalletTransactionDto(int Id, int UserId, string? UserName, string? UserEmail, decimal Amount, WalletTransactionStatus Status,
    Guid? ProofFileId, int? AuctionId, string? Note, DateTime CreatedAtUtc, DateTime? DecidedAtUtc);

public sealed record LedgerEntryDto(long Id, decimal Amount, decimal BalanceAfter, LedgerReason Reason, int? WalletTransactionId, DateTime CreatedAtUtc);

/// <summary>
/// The only code that changes <see cref="User.WalletBalance"/>. Every change is one database transaction that
/// (1) moves the transaction's status with a conditional UPDATE (so a repeated or concurrent request affects zero rows),
/// (2) adjusts the balance with an atomic UPDATE, and (3) appends a ledger entry (unique per transaction and reason).
/// </summary>
public sealed class WalletService(AppDbContext db, TimeProvider clock, Audit audit)
{
    public const decimal MaxTopUp = 1_000_000m;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Customer top-up request. Never touches the balance. A retry with the same idempotency key returns the original request.</summary>
    public async Task<WalletTransactionDto> RequestTopUpAsync(int userId, decimal amount, Guid? proofFileId, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount, MaxTopUp)) throw new AppException("wallet.invalid_amount", 400, new { max = MaxTopUp });
        idempotencyKey = Normalize.Text(idempotencyKey);
        if (idempotencyKey is { Length: > 100 }) throw new AppException("wallet.invalid_idempotency_key");

        if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } existing) return existing;

        var t = new WalletTransaction
        {
            UserId = userId, Amount = amount, Status = WalletTransactionStatus.Pending, ProofFileId = proofFileId,
            IdempotencyKey = idempotencyKey, CreatedByUserId = userId, CreatedAtUtc = Now,
        };
        db.WalletTransactions.Add(t);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // A concurrent duplicate won the unique index race: return that one.
            db.ChangeTracker.Clear();
            return await FindByKeyAsync(userId, idempotencyKey, ct) ?? throw new AppException("wallet.conflict", 409);
        }
        return (await GetAsync(t.Id, ct))!;
    }

    /// <summary>Administrator adds funds directly (already approved).</summary>
    public async Task<WalletTransactionDto> CreateApprovedCreditAsync(Actor admin, int userId, decimal amount, Guid? proofFileId, int? auctionId, string? note, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount, MaxTopUp)) throw new AppException("wallet.invalid_amount", 400, new { max = MaxTopUp });
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted, ct)) throw AppException.NotFound("user");
        if (auctionId is not null && !await db.Auctions.AnyAsync(a => a.Id == auctionId, ct)) throw AppException.NotFound("auction");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var t = new WalletTransaction
        {
            UserId = userId, Amount = amount, Status = WalletTransactionStatus.Approved, ProofFileId = proofFileId, AuctionId = auctionId,
            Note = Normalize.Text(note), CreatedByUserId = admin.UserId, CreatedAtUtc = Now, DecidedByUserId = admin.UserId, DecidedAtUtc = Now,
        };
        db.WalletTransactions.Add(t);
        await db.SaveChangesAsync(ct);
        await PostAsync(userId, amount, LedgerReason.AdminCredit, t.Id, admin.UserId, ct);
        audit.Record(admin, "wallet.credit", nameof(WalletTransaction), t.Id, new { userId, amount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(t.Id, ct))!;
    }

    /// <summary>Pending → Approved and credits the wallet exactly once. A second (or concurrent) call fails with wallet.not_pending.</summary>
    public Task<WalletTransactionDto> ApproveAsync(Actor admin, int id, CancellationToken ct = default) =>
        TransitionAsync(admin, id, WalletTransactionStatus.Pending, WalletTransactionStatus.Approved, +1, LedgerReason.TopUpApproved, NotificationKinds.TopUpApproved, ct);

    public Task<WalletTransactionDto> RejectAsync(Actor admin, int id, CancellationToken ct = default) =>
        TransitionAsync(admin, id, WalletTransactionStatus.Pending, WalletTransactionStatus.Rejected, 0, null, NotificationKinds.TopUpRejected, ct);

    /// <summary>Approved → Reversed and debits the credited amount (replaces the legacy "delete reverses an approved credit").
    /// The balance may go below zero if the funds were already used, exactly as in the legacy system.</summary>
    public Task<WalletTransactionDto> ReverseAsync(Actor admin, int id, CancellationToken ct = default) =>
        TransitionAsync(admin, id, WalletTransactionStatus.Approved, WalletTransactionStatus.Reversed, -1, LedgerReason.CreditReversed, null, ct);

    /// <summary>Only requests that never moved money (pending or rejected) can be deleted; approved credits must be reversed.</summary>
    public async Task DeleteAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var rows = await db.WalletTransactions
            .Where(t => t.Id == id && (t.Status == WalletTransactionStatus.Pending || t.Status == WalletTransactionStatus.Rejected))
            .ExecuteDeleteAsync(ct);
        if (rows == 0)
        {
            if (!await db.WalletTransactions.AnyAsync(t => t.Id == id, ct)) throw AppException.NotFound("transaction");
            throw AppException.Conflict("wallet.delete_not_allowed");
        }
        audit.Record(admin, "wallet.delete", nameof(WalletTransaction), id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The proof attachment is the only field that can change after creation.</summary>
    public async Task SetProofAsync(Actor admin, int id, Guid? fileId, CancellationToken ct = default)
    {
        var rows = await db.WalletTransactions.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ProofFileId, fileId), ct);
        if (rows == 0) throw AppException.NotFound("transaction");
        audit.Record(admin, "wallet.proof", nameof(WalletTransaction), id, new { fileId });
        await db.SaveChangesAsync(ct);
    }

    private async Task<WalletTransactionDto> TransitionAsync(Actor admin, int id, WalletTransactionStatus from, WalletTransactionStatus to,
        int sign, LedgerReason? reason, string? notificationKind, CancellationToken ct)
    {
        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var rows = await db.WalletTransactions.Where(t => t.Id == id && t.Status == from).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, to)
            .SetProperty(t => t.DecidedByUserId, admin.UserId)
            .SetProperty(t => t.DecidedAtUtc, now), ct);
        if (rows == 0)
        {
            await tx.RollbackAsync(ct);
            if (!await db.WalletTransactions.AnyAsync(t => t.Id == id, ct)) throw AppException.NotFound("transaction");
            throw AppException.Conflict(from == WalletTransactionStatus.Pending ? "wallet.not_pending" : "wallet.not_approved");
        }

        var t = await db.WalletTransactions.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (reason is not null) await PostAsync(t.UserId, sign * t.Amount, reason.Value, t.Id, admin.UserId, ct);
        if (notificationKind is not null)
            db.Notifications.Add(new Notification { UserId = t.UserId, FromUserId = admin.UserId, Kind = notificationKind, Amount = t.Amount, Url = "/account/wallet", CreatedAtUtc = now });
        audit.Record(admin, "wallet." + to.ToString().ToLowerInvariant(), nameof(WalletTransaction), id, new { t.UserId, t.Amount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    /// <summary>Atomic balance change plus its ledger row; must run inside the caller's transaction.</summary>
    private async Task PostAsync(int userId, decimal delta, LedgerReason reason, int? transactionId, int? actorId, CancellationToken ct)
    {
        var balances = await db.Database
            .SqlQuery<decimal>($"UPDATE Users SET WalletBalance = WalletBalance + {delta} OUTPUT inserted.WalletBalance AS Value WHERE Id = {userId}")
            .ToListAsync(ct);
        if (balances.Count != 1) throw AppException.NotFound("user");
        db.WalletLedger.Add(new WalletLedgerEntry
        {
            UserId = userId, Amount = delta, BalanceAfter = balances[0], Reason = reason, WalletTransactionId = transactionId,
            ActorUserId = actorId, CreatedAtUtc = Now,
        });
    }

    private Task<WalletTransactionDto?> FindByKeyAsync(int userId, string key, CancellationToken ct) =>
        Project(db.WalletTransactions.Where(t => t.UserId == userId && t.IdempotencyKey == key)).FirstOrDefaultAsync(ct);

    public Task<WalletTransactionDto?> GetAsync(int id, CancellationToken ct = default) =>
        Project(db.WalletTransactions.Where(t => t.Id == id)).FirstOrDefaultAsync(ct);

    public static IQueryable<WalletTransactionDto> Project(IQueryable<WalletTransaction> q) => q.Select(t => new WalletTransactionDto(
        t.Id, t.UserId, t.User!.FullName, t.User.Email, t.Amount, t.Status, t.ProofFileId, t.AuctionId, t.Note, t.CreatedAtUtc, t.DecidedAtUtc));

    public async Task<PagedResult<WalletTransactionDto>> ListAsync(int? userId, WalletTransactionStatus? status, string? search, DateTime? fromUtc, DateTime? toUtc, PageQuery page, CancellationToken ct = default)
    {
        var q = db.WalletTransactions.AsNoTracking();
        if (userId is not null) q = q.Where(t => t.UserId == userId);
        if (status is not null) q = q.Where(t => t.Status == status);
        if (fromUtc is not null) q = q.Where(t => t.CreatedAtUtc >= fromUtc);
        if (toUtc is not null) q = q.Where(t => t.CreatedAtUtc < toUtc);
        if (Normalize.Text(search) is { } s) q = q.Where(t => t.User!.FullName.Contains(s) || t.User.Email.Contains(s));
        var total = await q.CountAsync(ct);
        var items = await Project(q.OrderByDescending(t => t.Id)).Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<PagedResult<LedgerEntryDto>> LedgerAsync(int userId, PageQuery page, CancellationToken ct = default)
    {
        var q = db.WalletLedger.AsNoTracking().Where(l => l.UserId == userId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(l => l.Id).Skip(page.Skip).Take(page.SafeSize)
            .Select(l => new LedgerEntryDto(l.Id, l.Amount, l.BalanceAfter, l.Reason, l.WalletTransactionId, l.CreatedAtUtc)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }
}

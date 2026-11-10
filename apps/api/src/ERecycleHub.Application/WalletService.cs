using System.Data;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.Application;

public sealed record WalletTransactionDto(int Id, int UserId, string? UserName, string? UserEmail, decimal Amount, WalletTransactionStatus Status,
    Guid? ProofFileId, int? AuctionId, string? Note, DateTime CreatedAtUtc, DateTime? DecidedAtUtc, WalletTransactionType Type = WalletTransactionType.Credit);

public sealed record LedgerEntryDto(long Id, decimal Amount, decimal BalanceAfter, LedgerReason Reason, int? WalletTransactionId, DateTime CreatedAtUtc,
    decimal? BalanceBefore = null, string? ReferenceType = null, long? ReferenceId = null);

/// <summary><see cref="Available"/> is what the user can spend: the balance minus withdrawal requests still waiting for an administrator minus money held by their leading bids (<see cref="Held"/>).</summary>
public sealed record WalletBalanceDto(decimal Balance, decimal PendingWithdrawals, decimal Available, decimal Held = 0);

/// <summary>The balance around one posted ledger entry.</summary>
public sealed record WalletPosting(decimal Before, decimal After);

/// <summary>How far a debit may reduce the balance.</summary>
public enum DebitGuard
{
    /// <summary>Ordinary debit (admin reversal, withdrawal approval): the balance afterwards must still cover pending withdrawals and held bids.</summary>
    KeepHolds,
    /// <summary>The payment at auction close: it consumes the winner's own hold, so only "balance >= amount" is required.</summary>
    ConsumeHold,
}

/// <summary>
/// The only code that changes <see cref="User.WalletBalance"/>. Every change is one database transaction that
/// (1) moves the transaction's status with a conditional UPDATE (so a repeated or concurrent request affects zero rows),
/// (2) adjusts the balance with an atomic UPDATE, and (3) appends a ledger entry (unique per transaction and reason).
/// </summary>
public sealed class WalletService(AppDbContext db, TimeProvider clock, Audit audit, IBiddingSettings bidding, BackgroundWorkQueue queue, IConfiguration config)
{
    public const decimal MaxTopUp = 1_000_000m;

    /// <summary>Most Pending wallet requests (top-ups and withdrawals together) one user may have at a time (<c>Wallet:MaxPendingRequests</c>, default 5).</summary>
    private int MaxPendingRequests => Math.Clamp(config.GetValue("Wallet:MaxPendingRequests", 5), 1, 100);

    /// <summary>Locks the user's wallet row (the same lock bids and withdrawal requests take). Throws user.not_found when there is none.</summary>
    private async Task LockWalletAsync(int userId, CancellationToken ct)
    {
        if ((await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}").ToListAsync(ct)).Count != 1)
            throw AppException.NotFound("user");
    }

    /// <summary>Call under the user's wallet lock: refuses a new request when the user already has the maximum number Pending.</summary>
    private async Task EnsureRoomForRequestAsync(int userId, CancellationToken ct)
    {
        var max = MaxPendingRequests;
        if (await db.WalletTransactions.CountAsync(t => t.UserId == userId && t.Status == WalletTransactionStatus.Pending, ct) >= max)
            throw AppException.Conflict("wallet.too_many_pending", new { max });
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Customer top-up request. Never touches the balance. A retry with the same idempotency key returns the original request.</summary>
    public async Task<WalletTransactionDto> RequestTopUpAsync(int userId, decimal amount, Guid? proofFileId, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount, MaxTopUp)) throw new AppException("wallet.invalid_amount", 400, new { max = MaxTopUp });
        idempotencyKey = Normalize.Text(idempotencyKey);
        if (idempotencyKey is { Length: > 100 }) throw new AppException("wallet.invalid_idempotency_key");

        // Under the user's wallet lock, so concurrent retries and concurrent requests are serialized (one request per key, and the Pending cap holds).
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockWalletAsync(userId, ct);
        if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } existing)
            return existing.Type == WalletTransactionType.Credit ? existing : throw AppException.Conflict("wallet.idempotency_key_reused");
        await EnsureRoomForRequestAsync(userId, ct);

        var t = new WalletTransaction
        {
            UserId = userId, Amount = amount, Status = WalletTransactionStatus.Pending, ProofFileId = proofFileId,
            IdempotencyKey = idempotencyKey, CreatedByUserId = userId, CreatedAtUtc = Now,
        };
        db.WalletTransactions.Add(t);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(t.Id, ct))!;
    }

    /// <summary>
    /// Customer withdrawal request. Never touches the balance (an administrator pays it out and approves it). The user row is locked so
    /// concurrent requests cannot together ask for more than the balance minus the withdrawals already pending and the money held by leading bids.
    /// A retry with the same idempotency key returns the original request (409 wallet.idempotency_key_reused if the key was used for something else).
    /// </summary>
    public async Task<WalletTransactionDto> RequestWithdrawalAsync(int userId, decimal amount, string? idempotencyKey = null, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount, MaxTopUp)) throw new AppException("wallet.invalid_amount", 400, new { max = MaxTopUp });
        idempotencyKey = Normalize.Text(idempotencyKey);
        if (idempotencyKey is { Length: > 100 }) throw new AppException("wallet.invalid_idempotency_key");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var balances = await db.Database.SqlQuery<decimal>($"SELECT WalletBalance AS Value FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}").ToListAsync(ct);
        if (balances.Count != 1) throw AppException.NotFound("user");
        // Under the user's lock a concurrent duplicate has already committed, so a plain lookup is enough.
        if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } existing)
            return existing is { Type: WalletTransactionType.Withdrawal } && existing.Amount == amount ? existing : throw AppException.Conflict("wallet.idempotency_key_reused");
        await EnsureRoomForRequestAsync(userId, ct);
        var pending = await db.WalletTransactions
            .Where(t => t.UserId == userId && t.Type == WalletTransactionType.Withdrawal && t.Status == WalletTransactionStatus.Pending)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var held = await HeldAsync(userId, ct);   // money reserved by the user's leading bids cannot be withdrawn
        if (amount > balances[0] - pending - held) throw new AppException("wallet.insufficient", 422, new { available = Math.Max(0m, balances[0] - pending - held) });
        var t = new WalletTransaction
        {
            UserId = userId, Type = WalletTransactionType.Withdrawal, Amount = amount, Status = WalletTransactionStatus.Pending,
            IdempotencyKey = idempotencyKey, CreatedByUserId = userId, CreatedAtUtc = Now,
        };
        db.WalletTransactions.Add(t);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(t.Id, ct))!;
    }

    /// <summary>
    /// Administrator adds funds directly (already approved). Never to the administrator's own wallet (separation of duties: 403 wallet.self_credit_not_allowed,
    /// audited). An optional idempotency key makes a retry return the original credit instead of crediting twice (409 wallet.idempotency_key_reused when the key
    /// was used for a different amount); keys are per target user.
    /// </summary>
    public async Task<WalletTransactionDto> CreateApprovedCreditAsync(Actor admin, int userId, decimal amount, Guid? proofFileId, int? auctionId, string? note,
        string? idempotencyKey = null, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount, MaxTopUp)) throw new AppException("wallet.invalid_amount", 400, new { max = MaxTopUp });
        idempotencyKey = Normalize.Text(idempotencyKey);
        if (idempotencyKey is { Length: > 90 }) throw new AppException("wallet.invalid_idempotency_key");
        if (userId == admin.UserId)
        {
            audit.Record(admin, "wallet.credit_refused", nameof(User), userId, new { amount, reason = "self_credit" });
            await db.SaveChangesAsync(ct);
            throw new AppException("wallet.self_credit_not_allowed", 403);
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted, ct)) throw AppException.NotFound("user");
        if (auctionId is not null && !await db.Auctions.AnyAsync(a => a.Id == auctionId, ct)) throw AppException.NotFound("auction");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var credit = await CreditInTransactionAsync(admin, userId, amount, proofFileId, auctionId, note, idempotencyKey is null ? null : "admin:" + idempotencyKey, ct);
        await tx.CommitAsync(ct);
        return credit;
    }

    /// <summary>
    /// The credit itself, inside the caller's transaction (so creating a user and giving them an opening balance can be one transaction). The user's wallet row
    /// is locked first, then the request row is inserted and the balance changed, as everywhere else.
    /// </summary>
    internal async Task<WalletTransactionDto> CreditInTransactionAsync(Actor admin, int userId, decimal amount, Guid? proofFileId, int? auctionId, string? note,
        string? storedKey, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("The credit must run inside the caller's database transaction.");
        await LockWalletAsync(userId, ct);
        if (storedKey is not null && await FindByKeyAsync(userId, storedKey, ct) is { } existing)
            return existing is { Type: WalletTransactionType.Credit } && existing.Amount == amount ? existing : throw AppException.Conflict("wallet.idempotency_key_reused");

        var t = new WalletTransaction
        {
            UserId = userId, Amount = amount, Status = WalletTransactionStatus.Approved, ProofFileId = proofFileId, AuctionId = auctionId, IdempotencyKey = storedKey,
            Note = Normalize.Text(note), CreatedByUserId = admin.UserId, CreatedAtUtc = Now, DecidedByUserId = admin.UserId, DecidedAtUtc = Now,
        };
        db.WalletTransactions.Add(t);
        await db.SaveChangesAsync(ct);
        await PostAsync(userId, amount, LedgerReason.AdminCredit, t.Id, admin.UserId, ct);
        audit.Record(admin, "wallet.credit", nameof(WalletTransaction), t.Id, new { userId, amount });
        await db.SaveChangesAsync(ct);
        return (await GetAsync(t.Id, ct))!;
    }

    /// <summary>Top-up: Pending → Approved and credits the wallet exactly once. Withdrawal: Pending → Approved and debits the wallet exactly once, only if the
    /// available balance (balance − other pending withdrawals − money held by leading bids) still covers it (else 422 wallet.insufficient and the request stays Pending).
    /// A second (or concurrent) call fails with 409 wallet.not_pending. The user is notified once (in-app row + push).</summary>
    public async Task<WalletTransactionDto> ApproveAsync(Actor admin, int id, CancellationToken ct = default) =>
        await TypeOfAsync(id, ct) == WalletTransactionType.Withdrawal
            ? await TransitionAsync(admin, id, WalletTransactionStatus.Pending, WalletTransactionStatus.Approved, -1, LedgerReason.WithdrawalPaid, NotificationKinds.WithdrawalApproved, ct)
            : await TransitionAsync(admin, id, WalletTransactionStatus.Pending, WalletTransactionStatus.Approved, +1, LedgerReason.TopUpApproved, NotificationKinds.TopUpApproved, ct);

    public async Task<WalletTransactionDto> RejectAsync(Actor admin, int id, CancellationToken ct = default) =>
        await TransitionAsync(admin, id, WalletTransactionStatus.Pending, WalletTransactionStatus.Rejected, 0, null,
            await TypeOfAsync(id, ct) == WalletTransactionType.Withdrawal ? NotificationKinds.WithdrawalRejected : NotificationKinds.TopUpRejected, ct);

    private async Task<WalletTransactionType> TypeOfAsync(int id, CancellationToken ct) =>
        await db.WalletTransactions.Where(t => t.Id == id).Select(t => (WalletTransactionType?)t.Type).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("transaction");

    /// <summary>Approved → Reversed and debits the credited amount (replaces the legacy "delete reverses an approved credit").
    /// Refused with 422 wallet.insufficient when the money was already spent or is held by leading bids / pending withdrawals: the balance never goes below what is reserved.</summary>
    public async Task<WalletTransactionDto> ReverseAsync(Actor admin, int id, CancellationToken ct = default) =>
        await TypeOfAsync(id, ct) == WalletTransactionType.Withdrawal ? throw AppException.Conflict("wallet.reverse_not_allowed") : await TransitionAsync(admin, id, WalletTransactionStatus.Approved, WalletTransactionStatus.Reversed, -1, LedgerReason.CreditReversed, NotificationKinds.CreditReversed, ct);

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

    /// <summary>
    /// One decision on a request, atomically: lock the user's wallet row first (the same order bids and withdrawal requests use, so they cannot deadlock),
    /// move the status with a conditional UPDATE (a repeated or concurrent call affects zero rows), post the ledger entry through the guarded balance change,
    /// and write the in-app notification (unique per transaction and outcome). The push is queued after the commit.
    /// </summary>
    private async Task<WalletTransactionDto> TransitionAsync(Actor admin, int id, WalletTransactionStatus from, WalletTransactionStatus to,
        int sign, LedgerReason? reason, string? notificationKind, CancellationToken ct)
    {
        var now = Now;
        var owner = await db.WalletTransactions.AsNoTracking().Where(t => t.Id == id).Select(t => (int?)t.UserId).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("transaction");
        if (owner == admin.UserId && from == WalletTransactionStatus.Pending && to == WalletTransactionStatus.Approved)
        {
            // Separation of duties: nobody approves their own request (an administrator who is also a customer asks, someone else decides).
            audit.Record(admin, "wallet.approve_refused", nameof(WalletTransaction), id, new { userId = owner, reason = "self_approval" });
            await db.SaveChangesAsync(ct);
            throw new AppException("wallet.self_approval_not_allowed", 403);
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (reason is not null)
        {
            await LockWalletAsync(owner, ct);
            // Money is not moved for an account that is inactive or deleted (rejecting stays possible: it moves nothing). Checked under the wallet lock.
            if (!await db.Users.AsNoTracking().Where(u => u.Id == owner).Select(u => u.IsActive && !u.IsDeleted).SingleAsync(ct))
            {
                await tx.RollbackAsync(ct);
                audit.Record(admin, to == WalletTransactionStatus.Reversed ? "wallet.reverse_refused" : "wallet.approve_refused", nameof(WalletTransaction), id, new { userId = owner, reason = "owner_inactive" });
                await db.SaveChangesAsync(ct);
                throw AppException.Conflict("wallet.owner_inactive");
            }
        }

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
        if (reason is not null)
        {
            try { await PostAsync(t.UserId, sign * t.Amount, reason.Value, t.Id, admin.UserId, ct); }
            catch (AppException ex) when (ex.Code == "wallet.insufficient")
            {
                // The guard refused: nothing is posted and the request keeps its status. Say why, so the administrator can see what is reserved.
                db.ChangeTracker.Clear();
                await tx.RollbackAsync(ct);
                var b = await BalanceAsync(t.UserId, ct);
                // The refusal itself is recorded (outside the rolled-back transaction): who tried to push an unfunded decision through, and what was reserved.
                audit.Record(admin, to == WalletTransactionStatus.Reversed ? "wallet.reverse_refused" : "wallet.approve_refused", nameof(WalletTransaction), id,
                    new { t.UserId, t.Amount, reason = "insufficient", balance = b.Balance, held = b.Held });
                await db.SaveChangesAsync(ct);
                var otherPending = t.Type == WalletTransactionType.Withdrawal && from == WalletTransactionStatus.Pending ? b.PendingWithdrawals - t.Amount : b.PendingWithdrawals;
                throw new AppException("wallet.insufficient", 422, new
                {
                    required = t.Amount, available = Math.Max(0m, b.Balance - otherPending - b.Held), balance = b.Balance, pendingWithdrawals = otherPending, held = b.Held,
                });
            }
        }
        if (notificationKind is not null)
            db.Notifications.Add(new Notification
            {
                UserId = t.UserId, FromUserId = admin.UserId, Kind = notificationKind, Amount = t.Amount, Url = "/account/wallet", CreatedAtUtc = now,
                EventKey = WalletNotifier.EventKey(t.Id, to),
            });
        audit.Record(admin, "wallet." + to.ToString().ToLowerInvariant(), nameof(WalletTransaction), id, new { t.UserId, t.Amount });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (notificationKind is not null)
            queue.TryEnqueue($"notify.wallet:{id}:{to}", (sp, c) => sp.GetRequiredService<WalletNotifier>().NotifyDecisionAsync(id, to, c));
        return (await GetAsync(id, ct))!;
    }

    /// <summary>
    /// Takes money out of a wallet and records it in the ledger. Must run inside the caller's database transaction (the caller saves and
    /// commits, so the debit lives or dies with whatever else the caller does). Throws <c>wallet.insufficient</c> (422) rather than ever
    /// leaving a negative balance, even when many debits run at once: the check and the change are one UPDATE.
    /// </summary>
    public async Task<WalletPosting> DebitAsync(int userId, decimal amount, LedgerReason reason, string? referenceType, long? referenceId,
        int? actorUserId = null, CancellationToken ct = default, DebitGuard guard = DebitGuard.KeepHolds)
    {
        if (!Money.IsValidPositive(amount)) throw new AppException("wallet.invalid_amount");
        return await ApplyAsync(userId, -amount, reason, null, referenceType, referenceId, actorUserId, guard, ct);
    }

    /// <summary>Puts money into a wallet and records it in the ledger. Same transaction rules as <see cref="DebitAsync"/>.</summary>
    public async Task<WalletPosting> CreditAsync(int userId, decimal amount, LedgerReason reason, string? referenceType, long? referenceId,
        int? actorUserId = null, CancellationToken ct = default)
    {
        if (!Money.IsValidPositive(amount)) throw new AppException("wallet.invalid_amount");
        return await ApplyAsync(userId, amount, reason, null, referenceType, referenceId, actorUserId, DebitGuard.KeepHolds, ct);
    }

    /// <summary>Wallet-transaction postings (top-ups, withdrawals, admin credits, reversals) also record the transaction as their reference.</summary>
    private Task<WalletPosting> PostAsync(int userId, decimal delta, LedgerReason reason, int? transactionId, int? actorId, CancellationToken ct) =>
        ApplyAsync(userId, delta, reason, transactionId,
            transactionId is null ? null : LedgerReferences.WalletTransaction, transactionId, actorId, DebitGuard.KeepHolds, ct);

    private sealed class BalanceChange
    {
        public decimal Before { get; set; }
        public decimal After { get; set; }
    }

    /// <summary>The only place the balance is written. One conditional UPDATE changes it and reports before/after; a ledger row is queued.</summary>
    private async Task<WalletPosting> ApplyAsync(int userId, decimal delta, LedgerReason reason, int? transactionId, string? referenceType,
        long? referenceId, int? actorId, DebitGuard guard, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Wallet changes must run inside the caller's database transaction.");

        // A debit must leave the balance able to cover what is reserved for others: pending withdrawals and the money held by leading bids.
        // The payment at auction close consumes its own hold, so for it only "balance >= amount" is required. Credits are never blocked.
        var changes = delta < 0 && guard == DebitGuard.KeepHolds
            ? await db.Database.SqlQuery<BalanceChange>($@"UPDATE Users SET WalletBalance = WalletBalance + {delta}
OUTPUT deleted.WalletBalance AS Before, inserted.WalletBalance AS After
WHERE Id = {userId} AND WalletBalance + {delta} >=
  ISNULL((SELECT SUM(HeldAmount) FROM Bids WHERE UserId = {userId} AND ReleasedAtUtc IS NULL AND SettledAtUtc IS NULL), 0)
  + ISNULL((SELECT SUM(Amount) FROM WalletTransactions WHERE UserId = {userId} AND Type = 1 AND Status = 0), 0)").ToListAsync(ct)
            : await db.Database.SqlQuery<BalanceChange>(
                $"UPDATE Users SET WalletBalance = WalletBalance + {delta} OUTPUT deleted.WalletBalance AS Before, inserted.WalletBalance AS After WHERE Id = {userId} AND WalletBalance + {delta} >= 0")
                .ToListAsync(ct);
        if (changes.Count != 1)
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw AppException.NotFound("user");
            throw new AppException("wallet.insufficient", 422);
        }
        var c = changes[0];
        db.WalletLedger.Add(new WalletLedgerEntry
        {
            UserId = userId, Amount = delta, BalanceBefore = c.Before, BalanceAfter = c.After, Reason = reason, WalletTransactionId = transactionId,
            ReferenceType = referenceType, ReferenceId = referenceId, ActorUserId = actorId, CreatedAtUtc = Now,
        });
        return new WalletPosting(c.Before, c.After);
    }

    /// <summary>Withdrawal requests still waiting for an administrator. Stable while the user's wallet row is locked (requests lock it too).</summary>
    public async Task<decimal> PendingWithdrawalsAsync(int userId, CancellationToken ct = default) =>
        await db.WalletTransactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Type == WalletTransactionType.Withdrawal && t.Status == WalletTransactionStatus.Pending)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

    /// <summary>Money reserved by the user's leading bids (active holds: not released, not yet paid). Stable while the user's wallet row is locked (bids lock it too).</summary>
    public async Task<decimal> HeldAsync(int userId, CancellationToken ct = default) =>
        await db.Bids.AsNoTracking().Where(b => b.UserId == userId && b.ReleasedAtUtc == null && b.SettledAtUtc == null && b.HeldAmount > 0)
            .SumAsync(b => (decimal?)b.HeldAmount, ct) ?? 0m;

    /// <summary>
    /// The wallet figure shown to a user: with wallet holds on it is what they can spend (balance minus pending withdrawals minus held bids), otherwise
    /// the plain balance as before. Admin screens keep showing the plain balance.
    /// </summary>
    public async Task<decimal> ShownBalanceAsync(int userId, CancellationToken ct = default)
    {
        var b = await BalanceAsync(userId, ct);
        return bidding.DebitWallet ? b.Available : b.Balance;
    }

    /// <summary>Balance, pending withdrawal requests and what can be spent right now.</summary>
    public async Task<WalletBalanceDto> BalanceAsync(int userId, CancellationToken ct = default)
    {
        var balance = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (decimal?)u.WalletBalance).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("user");
        var pending = await PendingWithdrawalsAsync(userId, ct);
        var held = await HeldAsync(userId, ct);
        return new WalletBalanceDto(balance, pending, Math.Max(0m, balance - pending - held), held);
    }

    private Task<WalletTransactionDto?> FindByKeyAsync(int userId, string key, CancellationToken ct) =>
        Project(db.WalletTransactions.Where(t => t.UserId == userId && t.IdempotencyKey == key)).FirstOrDefaultAsync(ct);

    public Task<WalletTransactionDto?> GetAsync(int id, CancellationToken ct = default) =>
        Project(db.WalletTransactions.Where(t => t.Id == id)).FirstOrDefaultAsync(ct);

    public static IQueryable<WalletTransactionDto> Project(IQueryable<WalletTransaction> q) => q.Select(t => new WalletTransactionDto(
        t.Id, t.UserId, t.User!.FullName, t.User.Email, t.Amount, t.Status, t.ProofFileId, t.AuctionId, t.Note, t.CreatedAtUtc, t.DecidedAtUtc, t.Type));

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
            .Select(l => new LedgerEntryDto(l.Id, l.Amount, l.BalanceAfter, l.Reason, l.WalletTransactionId, l.CreatedAtUtc, l.BalanceBefore, l.ReferenceType, l.ReferenceId)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }
}

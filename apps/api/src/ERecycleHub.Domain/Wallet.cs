namespace ERecycleHub.Domain;

public enum WalletTransactionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    /// <summary>A previously approved credit that an administrator reversed (legacy: "delete" of an approved credit).</summary>
    Reversed = 3,
}

/// <summary>A wallet credit: a customer's top-up request (starts Pending) or an administrator's direct credit (starts Approved).
/// Legacy table [Transaction]; the only legacy type is "Credit".</summary>
public class WalletTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public decimal Amount { get; set; }
    public WalletTransactionStatus Status { get; set; }
    public Guid? ProofFileId { get; set; }
    public int? AuctionId { get; set; }
    public string? Note { get; set; }
    /// <summary>Client-supplied key so a retried request cannot create a second top-up. Unique per user.</summary>
    public string? IdempotencyKey { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

public enum LedgerReason
{
    MigrationOpeningBalance = 0,
    TopUpApproved = 1,
    AdminCredit = 2,
    CreditReversed = 3,
}

/// <summary>Append-only record of every balance change. Sum of entries per user equals <see cref="User.WalletBalance"/>.</summary>
public class WalletLedgerEntry
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public LedgerReason Reason { get; set; }
    public int? WalletTransactionId { get; set; }
    public int? ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

namespace ERecycleHub.Domain;

public enum WalletTransactionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    /// <summary>A previously approved credit that an administrator reversed (legacy: "delete" of an approved credit).</summary>
    Reversed = 3,
}

public enum WalletTransactionType
{
    /// <summary>Money in: a top-up request or an administrator credit.</summary>
    Credit = 0,
    /// <summary>Money out: a customer's withdrawal request. The balance is debited only when an administrator approves it.</summary>
    Withdrawal = 1,
}

/// <summary>A wallet credit: a customer's top-up request (starts Pending) or an administrator's direct credit (starts Approved).
/// Legacy table [Transaction]; the only legacy type is "Credit".</summary>
public class WalletTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public WalletTransactionType Type { get; set; }
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
    WithdrawalPaid = 4,
    /// <summary>OBSOLETE (debit-at-bid model, never enabled in production). Kept so old rows still read.</summary>
    BidDeduction = 5,
    /// <summary>OBSOLETE (debit-at-bid model).</summary>
    BidRefund = 6,
    /// <summary>OBSOLETE (debit-at-bid model).</summary>
    AuctionRefund = 7,
    /// <summary>OBSOLETE (debit-at-bid model).</summary>
    AuctionRehold = 8,
    /// <summary>The winner's held bid charged when the auction ended. The money goes to the platform (no seller payout).</summary>
    AuctionPayment = 9,
    /// <summary>One-time correction when converting bids held under the old debit-at-bid model: the amount held on running auctions is credited back so it is a hold again.</summary>
    HoldConversion = 10,
}

/// <summary>What <see cref="WalletLedgerEntry.ReferenceId"/> points at.</summary>
public static class LedgerReferences
{
    public const string WalletTransaction = "wallet_transaction";
    public const string Bid = "bid";
    public const string Auction = "auction";
}

/// <summary>Append-only record of every balance change. Sum of entries per user equals <see cref="User.WalletBalance"/>.</summary>
public class WalletLedgerEntry
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Balance before this entry. Null on rows written before the column existed.</summary>
    public decimal? BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public LedgerReason Reason { get; set; }
    public int? WalletTransactionId { get; set; }
    public int? ActorUserId { get; set; }
    /// <summary>What caused the entry (see <see cref="LedgerReferences"/>) and its id. Null on older rows.</summary>
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

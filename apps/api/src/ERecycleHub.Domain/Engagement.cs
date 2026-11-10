namespace ERecycleHub.Domain;

public class WishlistItem
{
    public int UserId { get; set; }
    public int AuctionId { get; set; }
    public Auction? Auction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>"Notify me" subscription: the user is told about new bids on this auction.</summary>
public class AuctionWatch
{
    public int UserId { get; set; }
    public int AuctionId { get; set; }
    public Auction? Auction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public static class NotificationKinds
{
    /// <summary>Free text migrated from the legacy table.</summary>
    public const string Legacy = "legacy";
    public const string NewBid = "auction.new_bid";
    public const string Outbid = "auction.outbid";
    public const string TopUpApproved = "wallet.topup_approved";
    public const string TopUpRejected = "wallet.topup_rejected";
    public const string WithdrawalApproved = "wallet.withdrawal_approved";
    public const string WithdrawalRejected = "wallet.withdrawal_rejected";
    /// <summary>An administrator reversed an approved credit (the amount was taken back).</summary>
    public const string CreditReversed = "wallet.credit_reversed";
    public const string AuctionWon = "auction.won";
    /// <summary>The user took part in an auction for the first time (their first bid).</summary>
    public const string Joined = "auction.joined";
    /// <summary>An auction the user took part in ended (they did not win).</summary>
    public const string Ended = "auction.ended";
    /// <summary>The user won but their wallet could not cover the winning amount at close.</summary>
    public const string PaymentFailed = "auction.payment_failed";
    public const string SellerApproved = "seller.approved";
    public const string SellerRejected = "seller.rejected";
}

/// <summary>In-app notification. <see cref="Kind"/> + auction fields let the UI render it in the reader's language.</summary>
public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? FromUserId { get; set; }
    public required string Kind { get; set; }
    public int? AuctionId { get; set; }
    public decimal? Amount { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Url { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Identifies the event this row announces to this user (e.g. <c>bid:123</c>, <c>join:42</c>, <c>end:42</c>); unique per user, so a repeated job cannot create a second row.</summary>
    public string? EventKey { get; set; }
    /// <summary>Set (once) when the push for this row was handed to the push service, so a repeated job cannot send it twice.</summary>
    public DateTime? PushedAtUtc { get; set; }
    /// <summary>Soft delete: hidden from the list but restorable (mobile "undo").</summary>
    public DateTime? DeletedAtUtc { get; set; }
}

public class Review
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public Auction? Auction { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    /// <summary>1..5</summary>
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ContactMessage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Mobile { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

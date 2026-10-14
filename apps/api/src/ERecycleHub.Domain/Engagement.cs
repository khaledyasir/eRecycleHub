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
    public const string AuctionWon = "auction.won";
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

namespace ERecycleHub.Domain;

public class Auction
{
    public int Id { get; set; }
    public required string RefNumber { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    /// <summary>Null for auctions created by an administrator on the platform's own behalf.</summary>
    public int? PublisherId { get; set; }
    public Publisher? Publisher { get; set; }
    /// <summary>Null for legacy auctions: the old schema had no auction/category relation and none is invented.</summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public string? ShortDescEn { get; set; }
    public string? ShortDescAr { get; set; }
    /// <summary>Sanitised HTML.</summary>
    public string? DescEn { get; set; }
    public string? DescAr { get; set; }
    // Legacy showed these three as PDF documents in iframes.
    public Guid? TermsFileId { get; set; }
    public Guid? PaymentConditionsFileId { get; set; }
    public Guid? ContractFileId { get; set; }
    public string? VideoUrl { get; set; }
    public Guid? MainImageFileId { get; set; }

    public decimal StartPrice { get; set; }
    /// <summary>Highest accepted bid, or <see cref="StartPrice"/> while there are none. Only the bidding service writes it.</summary>
    public decimal CurrentPrice { get; set; }
    public decimal BidIncrement { get; set; }
    /// <summary>Wallet balance a bidder must hold. Checked, never charged or held (legacy semantics).</summary>
    public decimal InsuranceAmount { get; set; }

    public DateTime StartsAtUtc { get; set; }
    /// <summary>Required for new auctions. Legacy rows may lack it; such auctions are treated as ended.</summary>
    public DateTime? EndsAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? WinnerNotifiedAtUtc { get; set; }
    public int BidCount { get; set; }

    public List<AuctionImage> Images { get; set; } = [];
    public List<AuctionSpec> Specs { get; set; } = [];
    public List<Bid> Bids { get; set; } = [];
}

public class AuctionImage
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public Guid FileId { get; set; }
    public string? Title { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Extra key/value rows on the auction page (legacy AdditionalInformation).</summary>
public class AuctionSpec
{
    public int Id { get; set; }
    public int AuctionId { get; set; }
    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public string? ValueEn { get; set; }
    public string? ValueAr { get; set; }
    public int SortOrder { get; set; }
}

public class Bid
{
    public long Id { get; set; }
    public int AuctionId { get; set; }
    public Auction? Auction { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum AuctionPhase
{
    Upcoming,
    Live,
    Ended,
}

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
    /// <summary>Set by the administrator's "notify winners" e-mail flow only. The automatic end job does not touch it.</summary>
    public DateTime? WinnerNotifiedAtUtc { get; set; }
    /// <summary>Claimed once by the automatic end job when it has settled the winner's hold and written the in-app rows. Separate from <c>WinnerNotifiedAtUtc</c>.</summary>
    public DateTime? EndProcessedAtUtc { get; set; }
    /// <summary>Set once when the winner's payment at close failed (insufficient balance). The close job keeps retrying; an administrator can void the sale.</summary>
    public DateTime? SettlementFailedAtUtc { get; set; }
    public int BidCount { get; set; }
    /// <summary>Push-notification topic of this auction (<c>auction_{Id}</c>). A computed database column: never set by code.</summary>
    public string? TopicName { get; private set; }

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
    /// <summary>
    /// Wallet money this bid RESERVES (the bid itself). The wallet balance and ledger are not touched by a bid. 0 for bids placed before wallet holds existed.
    /// It is an active hold while <see cref="ReleasedAtUtc"/> and <see cref="SettledAtUtc"/> are both null. Only the current leader holds.
    /// </summary>
    public decimal HeldAmount { get; set; }
    /// <summary>Set when the hold was released: the bidder was outbid, raised their own bid (the hold moved to the new bid), or the auction was deactivated. No ledger entry: nothing was ever debited.</summary>
    public DateTime? ReleasedAtUtc { get; set; }
    /// <summary>Set when the auction ended and this (winning) bid's hold was turned into a payment (an <c>AuctionPayment</c> ledger entry). A settled bid no longer holds.</summary>
    public DateTime? SettledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public enum AuctionPhase
{
    Upcoming,
    Live,
    Ended,
}

/// <summary>A user who has bid in an auction. One row per (auction, user): the composite key is the uniqueness guarantee.</summary>
public class AuctionParticipant
{
    public int AuctionId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}

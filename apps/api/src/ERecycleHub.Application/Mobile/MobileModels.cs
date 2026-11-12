using System.Text.Json.Serialization;

namespace ERecycleHub.Application.Mobile;

// Response shapes of the mobile API (docs/API_REQUIREMENTS.md §2). JSON keys are fixed by the app, including its spellings
// (isexpire, termAndConditon, transaction); do not rename them.

/// <summary>Absolute URL of a stored file, resolved by the host (the app loads images without the bearer token).</summary>
public interface IFileUrls
{
    string? Of(Guid? fileId);
}

public sealed class MobileOptions
{
    /// <summary>Currency code shown next to every amount.</summary>
    public string Currency { get; set; } = "JOD";
}

public sealed record AuthTokenDto(string Token, DateTime ExpiresAt);
public sealed record OtpVerifyDto(string? ResetToken);

public sealed record AuctionItemDto(
    int Id, int? CategoryId, string TitleEn, string TitleAr, string? MainImage, IReadOnlyList<string> Images,
    decimal CurrentPrice, decimal IncreaseRange, DateTime? EndDate, string Currency, int BidsCount,
    [property: JsonPropertyName("isexpire")] bool IsExpire);

public sealed record AuctionImageDto(int Id, string? Image);
public sealed record LatestBidDto(long Id, int UserId, decimal Amount, DateTime CreatedDate);
public sealed record AuctionDocumentDto(int Id, string Name, string Type, long SizeKb, string? Url);

/// <param name="Wallet">The viewer's available balance when wallet bidding is on, else the plain balance.</param>
/// <param name="IsLeading">The viewer holds the highest bid right now.</param>
/// <param name="HeldByViewer">Money the viewer's leading bid in this auction holds (0 when they are not leading or nothing is held).</param>
/// <param name="DebitsWallet">Wallet bidding is on: a bid holds its amount and the winner is charged at close.</param>
/// <param name="IsWinner">The auction has ended, its sale was not voided, and the viewer holds the winning bid.</param>
/// <param name="SaleStatus">Ended auctions with bids only (null otherwise): Paid | PaymentFailed | Pending | Voided | NotCharged (viewers other than the winning bidder only ever see Paid or Pending). See docs/MOBILE-API.md.</param>
/// <param name="AvailableAfter">Only in the response to a bid: the viewer's available balance right after it.</param>
/// <param name="ReleasedFromPrevious">Only in the response to a bid: the hold released from the previous leader (0 when there was none or it was the bidder's own).</param>
public sealed record AuctionDetailsDto(
    int Id, int? CategoryId, string TitleEn, string TitleAr, string? MainImage, string? ShortDescEn, string? ShortDescAr,
    string? DescEn, string? DescAr, decimal StartPrice, decimal CurrentPrice, decimal IncreaseRange,
    DateTime StartDate, DateTime? EndDate, string EndDateRemaining, string RefNumber, string Status,
    [property: JsonPropertyName("termAndConditon")] string? TermAndConditon,
    string? Video, string? Contract, string? ConditionForPayment, string? PreviewTime, decimal InsuranceAmount, decimal Wallet,
    IReadOnlyList<AuctionImageDto> Images, int BidsCount,
    [property: JsonPropertyName("isexpire")] bool IsExpire,
    string Currency, IReadOnlyList<LatestBidDto> LatestBids, IReadOnlyList<AuctionDocumentDto> Documents,
    bool IsLeading = false, decimal HeldByViewer = 0, bool DebitsWallet = false, bool IsWinner = false, string? SaleStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? AvailableAfter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ReleasedFromPrevious = null);

public sealed record CategoryDto(int Id, string TitleEn, string TitleAr, string? Icon, string? EndTime, int AuctionCount, IReadOnlyList<AuctionItemDto> Auctions);
public sealed record AuctionStatusDto(int Id, string TitleEn, string TitleAr, int AuctionCount, IReadOnlyList<AuctionItemDto> Auctions);

public sealed record SlideDto(int Id, string? Image, string? Title, string? TitleAr, string? Description, string? DescriptionAr,
    string? ButtonText, string? ButtonTextAr, string? Link);
public sealed record MobileCountryDto(int Id, string Name, string NameAr, string? Image, string? Code);
public sealed record ContentDto(int Id, string? TitleEn, string? TitleAr, string? ValueEn, string? ValueAr);
public sealed record FaqItemDto(int Id, string QuestionEn, string QuestionAr, string? AnswerEn, string? AnswerAr);
public sealed record ContactUsDto(string? Mobile, string? Email, string? Address, string? Map, string? Facebook, string? Instagram, string? Twitter, string? Linkedin);

public sealed record UserProfileDto(int Id, string FullName, string Email, string? Phone, string? Image);
public sealed record ProfileImageDto(string? Image);
public sealed record MyAuctionsDto(IReadOnlyList<AuctionItemDto> Live, IReadOnlyList<AuctionItemDto> Won);

/// <summary>
/// One line of the wallet history. Real wallet transactions have type Deposit | Withdraw. Display-only hold lines (built from the user's bids, never stored, no money moves)
/// have type Hold | HoldRelease, a NEGATIVE id (so they never clash with a transaction id) and the auction they belong to.
/// </summary>
public sealed record WalletTransactionItemDto(int Id, string Type, decimal Amount, string Status, DateTime CreatedDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AuctionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuctionTitle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuctionTitleAr = null);
public sealed record LedgerItemDto(long Id, string Reason, decimal Amount, decimal? BalanceBefore, decimal BalanceAfter, string? ReferenceType, long? ReferenceId, DateTime CreatedDate);
public sealed record WalletDto(decimal Wallet, [property: JsonPropertyName("transaction")] IReadOnlyList<WalletTransactionItemDto> Transaction);

/// <param name="Type">The chip: auction | bid | wallet | system.</param>
/// <param name="Kind">The exact event (auction.new_bid, auction.outbid, auction.joined, auction.won, auction.ended, auction.payment_failed, wallet.*, ...). Same as the push <c>type</c> with the dot turned into an underscore.</param>
/// <param name="AuctionId">The auction to open when the notification is tapped, when it is about one.</param>
public sealed record AppNotificationDto(int Id, string Type, string TitleEn, string TitleAr, string BodyEn, string BodyAr, DateTime CreatedAt, bool Read,
    string? Kind = null, int? AuctionId = null, decimal? Amount = null);

/// <summary>Bid accepted: the live event payload, also used to dedupe the bidder's own HTTP response.</summary>
public sealed record BidEventDto(decimal CurrentPrice, int BidsCount, LatestBidDto Bid);

/// <summary>A page of a mobile list: the items (a plain JSON array in the response) and the total across all pages (sent in the X-Total-Count header).</summary>
public sealed record MobileList<T>(List<T> Items, int Total);

/// <summary>Optional paging for the mobile lists: <c>page</c> (from 1) and <c>pageSize</c> (default and maximum 100). Ordering is never changed by paging.</summary>
public static class MobilePaging
{
    public const int DefaultSize = 100, MaxSize = 100;
    public static (int Page, int Size) Of(int? page, int? pageSize) => (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultSize, 1, MaxSize));
}

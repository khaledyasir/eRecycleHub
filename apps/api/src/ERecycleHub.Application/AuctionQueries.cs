using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record PublisherRef(int Id, string Name, Guid? LogoFileId);
public sealed record CategoryRef(int Id, string TitleEn, string TitleAr);
public sealed record FileRef(Guid Id, string Name, string ContentType);

public sealed record AuctionCard(
    int Id, string RefNumber, string TitleEn, string TitleAr, string? ShortDescEn, string? ShortDescAr, Guid? MainImageFileId,
    decimal StartPrice, decimal CurrentPrice, decimal BidIncrement, decimal InsuranceAmount,
    DateTime StartsAtUtc, DateTime? EndsAtUtc, AuctionPhase Phase, int BidCount, int CountryId, bool IsActive,
    PublisherRef? Publisher, CategoryRef? Category, bool IsFavorite, bool IsWatched);

public sealed record AuctionFilter(
    string? Status = null, string? Q = null, int[]? CategoryIds = null, decimal? MinPrice = null, decimal? MaxPrice = null,
    string? Period = null, string? Sort = null, int? CountryId = null, int? StoreId = null, int Page = 1, int PageSize = 12);

public sealed record CategoryFacet(int Id, string TitleEn, string TitleAr, Guid? IconFileId, int Count);
public sealed record AuctionFacets(IReadOnlyList<CategoryFacet> Categories, int Today, int Yesterday, int Last7Days, int Last30Days, decimal MaxPrice);

public sealed record SpecDto(int Id, string TitleEn, string TitleAr, string? ValueEn, string? ValueAr, int SortOrder);
public sealed record ImageDto(int Id, Guid FileId, string? Title, int SortOrder);
public sealed record PublisherSummary(int Id, string Name, Guid? LogoFileId, Guid? BackgroundFileId, string? Phone, string? SecondMobile, string? Email, string? Address, int AuctionCount);
public sealed record ViewerState(bool IsSignedIn, bool IsOwner, bool IsFavorite, bool IsWatched, bool HasReviewed, decimal? WalletBalance, bool CanBid, bool IsLeading);
public sealed record ReviewSummary(double Average, int Count);

public sealed record AuctionDetails(
    AuctionCard Card, string? DescEn, string? DescAr, string? VideoUrl, string CountryNameEn, string CountryNameAr,
    FileRef? Terms, FileRef? PaymentConditions, FileRef? Contract, IReadOnlyList<ImageDto> Images, IReadOnlyList<SpecDto> Specs,
    PublisherSummary? Publisher, decimal MinimumNextBid, DateTime CreatedAtUtc, ViewerState Viewer, ReviewSummary Reviews,
    IReadOnlyList<AuctionCard> Related);

public sealed record PublicBid(long Id, decimal Amount, DateTime CreatedAtUtc, int BidderNumber, bool IsMine);
public sealed record CalendarItem(int Id, string TitleEn, string TitleAr, DateTime StartsAtUtc, DateTime? EndsAtUtc, AuctionPhase Phase);
public sealed record ReviewDto(int Id, int AuctionId, string? AuctionTitleEn, string? AuctionTitleAr, string? Name, int Rating, string? Comment, DateTime CreatedAtUtc, Guid? AvatarFileId);

public sealed class AuctionQueries(AppDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Auctions anyone may see: active, and (if it has one) published by an active publisher.</summary>
    public IQueryable<Auction> Visible() =>
        db.Auctions.AsNoTracking().Where(a => a.IsActive && (a.PublisherId == null || a.Publisher!.User!.IsActive));

    public static IQueryable<Auction> InPhase(IQueryable<Auction> q, string? status, DateTime now) => status switch
    {
        "live" => q.Where(a => a.StartsAtUtc <= now && a.EndsAtUtc > now),
        "upcoming" => q.Where(a => a.StartsAtUtc > now),
        "ended" => q.Where(a => a.StartsAtUtc <= now && (a.EndsAtUtc == null || a.EndsAtUtc <= now)),
        _ => q,
    };

    /// <summary>Single SQL query projecting cards, including the viewer's favourite/watch flags.</summary>
    public static IQueryable<AuctionCard> ToCards(IQueryable<Auction> q, AppDbContext db, int? viewerId) => q.Select(a => new AuctionCard(
        a.Id, a.RefNumber, a.TitleEn, a.TitleAr, a.ShortDescEn, a.ShortDescAr, a.MainImageFileId,
        a.StartPrice, a.CurrentPrice, a.BidIncrement, a.InsuranceAmount, a.StartsAtUtc, a.EndsAtUtc, AuctionPhase.Upcoming, a.BidCount, a.CountryId, a.IsActive,
        a.Publisher == null ? null : new PublisherRef(a.Publisher.Id, a.Publisher.FirstName + (a.Publisher.LastName == "" ? "" : " " + a.Publisher.LastName), a.Publisher.LogoFileId),
        a.Category == null ? null : new CategoryRef(a.Category.Id, a.Category.TitleEn, a.Category.TitleAr),
        viewerId != null && db.Wishlist.Any(w => w.UserId == viewerId && w.AuctionId == a.Id),
        viewerId != null && db.AuctionWatches.Any(w => w.UserId == viewerId && w.AuctionId == a.Id)));

    public AuctionCard WithPhase(AuctionCard c) => c with { Phase = AuctionRules.Phase(c.StartsAtUtc, c.EndsAtUtc, Now) };

    public async Task<List<AuctionCard>> CardsAsync(IQueryable<Auction> q, int? viewerId, CancellationToken ct) =>
        (await ToCards(q, db, viewerId).ToListAsync(ct)).Select(WithPhase).ToList();

    public async Task<PagedResult<AuctionCard>> SearchAsync(AuctionFilter f, int? viewerId, CancellationToken ct = default)
    {
        var now = Now;
        var q = Visible();
        if (f.CountryId is > 0) q = q.Where(a => a.CountryId == f.CountryId);
        if (f.StoreId is > 0) q = q.Where(a => a.PublisherId == f.StoreId);
        q = InPhase(q, f.Status, now);
        if (f.CategoryIds is { Length: > 0 } cats) q = q.Where(a => a.CategoryId != null && cats.Contains(a.CategoryId.Value));
        if (Normalize.Text(f.Q) is { } term) q = q.Where(a => a.TitleEn.Contains(term) || a.TitleAr.Contains(term) || a.RefNumber == term);
        if (f.MinPrice is not null) q = q.Where(a => a.CurrentPrice >= f.MinPrice);
        if (f.MaxPrice is not null) q = q.Where(a => a.CurrentPrice <= f.MaxPrice);
        if (PeriodStart(f.Period, now) is var (from, to) && from is not null) q = q.Where(a => a.StartsAtUtc >= from && a.StartsAtUtc < to);

        q = f.Sort switch
        {
            "price_asc" => q.OrderBy(a => a.CurrentPrice).ThenBy(a => a.Id),
            "price_desc" => q.OrderByDescending(a => a.CurrentPrice).ThenBy(a => a.Id),
            "ending" => q.OrderBy(a => a.EndsAtUtc == null).ThenBy(a => a.EndsAtUtc).ThenBy(a => a.Id),
            "bids" => q.OrderByDescending(a => a.BidCount).ThenByDescending(a => a.Id),
            _ => q.OrderByDescending(a => a.StartsAtUtc).ThenByDescending(a => a.Id),
        };

        var page = new PageQuery(f.Page, Math.Clamp(f.PageSize, 1, 48));
        var total = await q.CountAsync(ct);
        var items = await CardsAsync(q.Skip(page.Skip).Take(page.SafeSize), viewerId, ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>Legacy period filter (by start date): today, yesterday, last 7 / 30 days. Days are UTC days.</summary>
    private static (DateTime? from, DateTime to) PeriodStart(string? period, DateTime now)
    {
        var today = now.Date;
        return period switch
        {
            "today" => (today, DateTime.MaxValue),
            "yesterday" => (today.AddDays(-1), today),
            "last7days" => (today.AddDays(-7), DateTime.MaxValue),
            "last30days" => (today.AddDays(-30), DateTime.MaxValue),
            _ => (null, DateTime.MaxValue),
        };
    }

    public async Task<AuctionFacets> FacetsAsync(int? countryId, int? storeId, CancellationToken ct = default)
    {
        var now = Now;
        var today = now.Date;
        var q = Visible();
        if (countryId is > 0) q = q.Where(a => a.CountryId == countryId);
        if (storeId is > 0) q = q.Where(a => a.PublisherId == storeId);

        var counts = await q.GroupBy(_ => 1).Select(g => new
        {
            Today = g.Count(a => a.StartsAtUtc >= today),
            Yesterday = g.Count(a => a.StartsAtUtc >= today.AddDays(-1) && a.StartsAtUtc < today),
            Last7 = g.Count(a => a.StartsAtUtc >= today.AddDays(-7)),
            Last30 = g.Count(a => a.StartsAtUtc >= today.AddDays(-30)),
            Max = g.Max(a => a.CurrentPrice),
        }).FirstOrDefaultAsync(ct);

        var categories = (await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && (countryId == null || countryId <= 0 || c.CountryId == countryId))
            .Select(c => new { c.Id, c.TitleEn, c.TitleAr, c.IconFileId, Count = q.Count(a => a.CategoryId == c.Id) })
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count).ThenBy(c => c.TitleEn)
            .ToListAsync(ct)).Select(c => new CategoryFacet(c.Id, c.TitleEn, c.TitleAr, c.IconFileId, c.Count)).ToList();

        return new(categories, counts?.Today ?? 0, counts?.Yesterday ?? 0, counts?.Last7 ?? 0, counts?.Last30 ?? 0, counts?.Max ?? 0);
    }

    /// <summary>Public details. Inactive auctions (or those of an inactive publisher) are only visible to their owner and administrators.</summary>
    public async Task<AuctionDetails?> DetailsAsync(int id, Actor? viewer, CancellationToken ct = default)
    {
        var a = await db.Auctions.AsNoTracking()
            .Include(x => x.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .Include(x => x.Specs.OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
            .Include(x => x.Country)
            .Include(x => x.Category)
            .Include(x => x.Publisher).ThenInclude(p => p!.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;

        var isOwner = viewer is not null && a.Publisher is not null && a.Publisher.UserId == viewer.UserId;
        var publicVisible = a.IsActive && (a.Publisher is null || a.Publisher.User!.IsActive);
        if (!publicVisible && !(isOwner || viewer?.IsAdmin == true)) return null;

        var now = Now;
        var vid = viewer?.UserId;
        var fileIds = new[] { a.TermsFileId, a.PaymentConditionsFileId, a.ContractFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var docs = await db.Files.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => new FileRef(f.Id, f.OriginalName, f.ContentType), ct);
        FileRef? Doc(Guid? fid) => fid is not null && docs.TryGetValue(fid.Value, out var d) ? d : null;

        var reviews = await db.Reviews.Where(r => r.AuctionId == id).GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() }).FirstOrDefaultAsync(ct);
        var publisherAuctionCount = a.PublisherId is null ? 0 : await Visible().CountAsync(x => x.PublisherId == a.PublisherId, ct);

        bool fav = false, watch = false, reviewed = false, leading = false;
        decimal? wallet = null;
        var role = viewer?.Role;
        if (vid is not null)
        {
            fav = await db.Wishlist.AnyAsync(w => w.UserId == vid && w.AuctionId == id, ct);
            watch = await db.AuctionWatches.AnyAsync(w => w.UserId == vid && w.AuctionId == id, ct);
            reviewed = await db.Reviews.AnyAsync(r => r.UserId == vid && r.AuctionId == id, ct);
            wallet = await db.Users.Where(u => u.Id == vid).Select(u => (decimal?)u.WalletBalance).FirstOrDefaultAsync(ct);
            leading = await db.Bids.Where(b => b.AuctionId == id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => (int?)b.UserId).FirstOrDefaultAsync(ct) == vid;
        }

        var phase = AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, now);
        var canBid = vid is not null && publicVisible && phase == AuctionPhase.Live && !isOwner && role != UserRole.Admin && wallet >= a.InsuranceAmount;

        var card = new AuctionCard(a.Id, a.RefNumber, a.TitleEn, a.TitleAr, a.ShortDescEn, a.ShortDescAr, a.MainImageFileId, a.StartPrice, a.CurrentPrice,
            a.BidIncrement, a.InsuranceAmount, a.StartsAtUtc, a.EndsAtUtc, phase, a.BidCount, a.CountryId, a.IsActive,
            a.Publisher is null ? null : new PublisherRef(a.Publisher.Id, a.Publisher.DisplayName, a.Publisher.LogoFileId),
            a.Category is null ? null : new CategoryRef(a.Category.Id, a.Category.TitleEn, a.Category.TitleAr), fav, watch);

        var relatedQ = Visible().Where(x => x.Id != id && x.CountryId == a.CountryId &&
            (x.PublisherId == a.PublisherId || (a.CategoryId != null && x.CategoryId == a.CategoryId)));
        var related = await CardsAsync(InPhase(relatedQ, "live", now).OrderBy(x => x.EndsAtUtc).Take(8), vid, ct);
        if (related.Count < 4)
        {
            var taken = related.Select(r => r.Id).Append(id).ToList();
            related.AddRange(await CardsAsync(InPhase(Visible().Where(x => x.CountryId == a.CountryId && !taken.Contains(x.Id)), "live", now)
                .OrderBy(x => x.EndsAtUtc).Take(8 - related.Count), vid, ct));
        }

        return new AuctionDetails(card, a.DescEn, a.DescAr, a.VideoUrl, a.Country!.NameEn, a.Country.NameAr,
            Doc(a.TermsFileId), Doc(a.PaymentConditionsFileId), Doc(a.ContractFileId),
            a.Images.Select(i => new ImageDto(i.Id, i.FileId, i.Title, i.SortOrder)).ToList(),
            a.Specs.Select(s => new SpecDto(s.Id, s.TitleEn, s.TitleAr, s.ValueEn, s.ValueAr, s.SortOrder)).ToList(),
            a.Publisher is null ? null : new PublisherSummary(a.Publisher.Id, a.Publisher.DisplayName, a.Publisher.LogoFileId, a.Publisher.BackgroundFileId,
                a.Publisher.User!.Phone, a.Publisher.SecondMobile, a.Publisher.User.Email, a.Publisher.Address, publisherAuctionCount),
            AuctionRules.MinimumNextBid(a.CurrentPrice, a.BidIncrement), a.CreatedAtUtc,
            new ViewerState(vid is not null, isOwner, fav, watch, reviewed, wallet, canBid, leading),
            new ReviewSummary(Math.Round(reviews?.Avg ?? 0, 1), reviews?.Count ?? 0), related);
    }

    /// <summary>Public bid history: amounts and times only. Bidders appear as "Bidder #n" (order of their first bid on this auction).</summary>
    public async Task<PagedResult<PublicBid>> PublicBidsAsync(int auctionId, int? viewerId, PageQuery page, CancellationToken ct = default)
    {
        if (!await Visible().AnyAsync(a => a.Id == auctionId, ct)) throw AppException.NotFound("auction");
        var q = db.Bids.AsNoTracking().Where(b => b.AuctionId == auctionId);
        var total = await q.CountAsync(ct);
        var firstBids = await q.GroupBy(b => b.UserId).Select(g => new { UserId = g.Key, First = g.Min(b => b.Id) }).ToListAsync(ct);
        var number = firstBids.OrderBy(x => x.First).Select((x, i) => (x.UserId, n: i + 1)).ToDictionary(x => x.UserId, x => x.n);
        var rows = await q.OrderByDescending(b => b.Id).Skip(page.Skip).Take(page.SafeSize).Select(b => new { b.Id, b.Amount, b.CreatedAtUtc, b.UserId }).ToListAsync(ct);
        var items = rows.Select(b => new PublicBid(b.Id, b.Amount, b.CreatedAtUtc, number[b.UserId], b.UserId == viewerId)).ToList();
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<List<CalendarItem>> CalendarAsync(DateTime fromUtc, DateTime toUtc, int? countryId, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(62)) throw new AppException("calendar.invalid_range");
        var q = Visible().Where(a => a.StartsAtUtc < toUtc && (a.EndsAtUtc == null ? a.StartsAtUtc >= fromUtc : a.EndsAtUtc >= fromUtc));
        if (countryId is > 0) q = q.Where(a => a.CountryId == countryId);
        var rows = await q.OrderBy(a => a.StartsAtUtc).Take(500).Select(a => new { a.Id, a.TitleEn, a.TitleAr, a.StartsAtUtc, a.EndsAtUtc }).ToListAsync(ct);
        var now = Now;
        return rows.Select(a => new CalendarItem(a.Id, a.TitleEn, a.TitleAr, a.StartsAtUtc, a.EndsAtUtc, AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, now))).ToList();
    }

    public async Task<PagedResult<ReviewDto>> ReviewsAsync(IQueryable<Review> q, PageQuery page, CancellationToken ct = default)
    {
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.Id).Skip(page.Skip).Take(page.SafeSize)
            .Select(r => new ReviewDto(r.Id, r.AuctionId, r.Auction!.TitleEn, r.Auction.TitleAr, r.Name ?? r.User!.FullName, r.Rating, r.Comment, r.CreatedAtUtc, r.User!.AvatarFileId))
            .ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public Task<PagedResult<ReviewDto>> AuctionReviewsAsync(int auctionId, int page, CancellationToken ct = default) =>
        ReviewsAsync(db.Reviews.AsNoTracking().Where(r => r.AuctionId == auctionId), new PageQuery(page, 10), ct);
}

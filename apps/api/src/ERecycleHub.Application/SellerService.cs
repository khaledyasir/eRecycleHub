using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record SellerDashboard(int Total, int Live, int Upcoming, int Ended, int Inactive, int Bids, int Bidders, decimal HighestBid,
    int Reviews, double RatingAverage, IReadOnlyList<ManagedBid> RecentBids, IReadOnlyList<ReviewDto> RecentReviews);
public sealed record BidderRow(int UserId, string FullName, string Email, string? Phone, int BidCount, decimal HighestBid, DateTime LastBidAtUtc);
public sealed record BidderBid(long Id, int AuctionId, string TitleEn, string TitleAr, decimal Amount, DateTime CreatedAtUtc);
public sealed record BidderDetails(BidderRow Bidder, string? CompanyName, IReadOnlyList<BidderBid> Bids);

public sealed record StoreProfileDto(int PublisherId, string FirstName, string LastName, string? Address, string? SecondMobile, int CountryId,
    Guid? LogoFileId, Guid? BackgroundFileId, string Email, string? Phone, string? CompanyName, string? CrNumber, string? VatNumber,
    string? BankName, string? AccountNumber, string? CardNumber, FileRef? CrDocument, FileRef? VatCertificate, FileRef? IdDocument);
public sealed record StoreProfileInput(string FirstName, string? LastName, string? Address, string? SecondMobile, int CountryId, string? Phone,
    string? CompanyName, string? CrNumber, string? VatNumber, string? BankName, string? AccountNumber, string? CardNumber);
public sealed record StoreProfileFiles(UploadInput? Logo, UploadInput? Background, UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);

/// <summary>Publisher portal reads. Every query is filtered by the caller's own publisher id.</summary>
public sealed class SellerService(AppDbContext db, AuctionQueries auctions, FileService files, TimeProvider clock, Audit audit)
{
    private static int Pid(Actor a) => a.PublisherId ?? throw AppException.Forbidden();

    public async Task<SellerDashboard> DashboardAsync(Actor actor, CancellationToken ct = default)
    {
        var pid = Pid(actor);
        var now = clock.GetUtcNow().UtcDateTime;
        var mine = db.Auctions.AsNoTracking().Where(a => a.PublisherId == pid);
        var active = mine.Where(a => a.IsActive);
        var bids = db.Bids.AsNoTracking().Where(b => b.Auction!.PublisherId == pid);
        var reviews = db.Reviews.AsNoTracking().Where(r => r.Auction!.PublisherId == pid);
        var rating = await reviews.GroupBy(_ => 1).Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() }).FirstOrDefaultAsync(ct);

        var recentBids = await bids.OrderByDescending(b => b.Id).Take(8)
            .Select(b => new ManagedBid(b.Id, b.UserId, b.User!.FullName, b.User.Phone, b.User.Email, b.Amount, b.CreatedAtUtc, false)).ToListAsync(ct);
        var recentReviews = (await auctions.ReviewsAsync(reviews, new PageQuery(1, 5), ct)).Items;

        return new SellerDashboard(
            await mine.CountAsync(ct),
            await AuctionQueries.InPhase(active, "live", now).CountAsync(ct),
            await AuctionQueries.InPhase(active, "upcoming", now).CountAsync(ct),
            await AuctionQueries.InPhase(active, "ended", now).CountAsync(ct),
            await mine.CountAsync(a => !a.IsActive, ct),
            await bids.CountAsync(ct),
            await bids.Select(b => b.UserId).Distinct().CountAsync(ct),
            await bids.MaxAsync(b => (decimal?)b.Amount, ct) ?? 0,
            rating?.Count ?? 0, Math.Round(rating?.Avg ?? 0, 1), recentBids, recentReviews);
    }

    public Task<PagedResult<ReviewDto>> ReviewsAsync(Actor actor, PageQuery page, CancellationToken ct = default) =>
        auctions.ReviewsAsync(db.Reviews.AsNoTracking().Where(r => r.Auction!.PublisherId == Pid(actor)), page, ct);

    public async Task DeleteReviewAsync(Actor actor, int id, CancellationToken ct = default)
    {
        var pid = Pid(actor);
        if (await db.Reviews.Where(r => r.Id == id && r.Auction!.PublisherId == pid).ExecuteDeleteAsync(ct) == 0) throw AppException.NotFound("review");
        audit.Record(actor, "review.delete", nameof(Review), id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Accounts that bid on this publisher's auctions, most recent bidder first. Filters apply to users before the record projection.</summary>
    private IQueryable<BidderRow> Bidders(int pid, string? term = null, int? userId = null)
    {
        var bids = db.Bids.Where(b => b.Auction!.PublisherId == pid);
        var users = db.Users.AsNoTracking().Where(u => bids.Any(b => b.UserId == u.Id));
        if (term is not null) users = users.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        if (userId is not null) users = users.Where(u => u.Id == userId);
        return users.Select(u => new
        {
            u.Id, u.FullName, u.Email, u.Phone,
            Count = bids.Count(b => b.UserId == u.Id),
            Highest = bids.Where(b => b.UserId == u.Id).Max(b => b.Amount),
            Last = bids.Where(b => b.UserId == u.Id).Max(b => b.CreatedAtUtc),
        }).OrderByDescending(x => x.Last).ThenBy(x => x.Id)
            .Select(x => new BidderRow(x.Id, x.FullName, x.Email, x.Phone, x.Count, x.Highest, x.Last));
    }

    /// <summary>People who bid on this publisher's auctions (legacy "Users" page, with its wrong filter fixed).</summary>
    public async Task<PagedResult<BidderRow>> BiddersAsync(Actor actor, string? q, PageQuery page, CancellationToken ct = default)
    {
        var rows = Bidders(Pid(actor), Normalize.Text(q));
        var total = await rows.CountAsync(ct);
        var items = await rows.Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>Contact details plus bids on *my* auctions only. Wallet, bank details, documents and other activity are not exposed (legacy IDOR fixed).</summary>
    public async Task<BidderDetails> BidderAsync(Actor actor, int userId, CancellationToken ct = default)
    {
        var pid = Pid(actor);
        var row = await Bidders(pid, userId: userId).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("user");
        var company = await db.Users.Where(u => u.Id == userId).Select(u => u.CompanyName).SingleAsync(ct);
        var bids = await db.Bids.AsNoTracking().Where(b => b.UserId == userId && b.Auction!.PublisherId == pid).OrderByDescending(b => b.Id).Take(200)
            .Select(b => new BidderBid(b.Id, b.AuctionId, b.Auction!.TitleEn, b.Auction.TitleAr, b.Amount, b.CreatedAtUtc)).ToListAsync(ct);
        return new BidderDetails(row, company, bids);
    }

    public async Task<StoreProfileDto> ProfileAsync(Actor actor, CancellationToken ct = default)
    {
        var pid = Pid(actor);
        var p = await db.Publishers.AsNoTracking().Include(x => x.User).SingleAsync(x => x.Id == pid, ct);
        var u = p.User!;
        var ids = new[] { u.CrDocumentFileId, u.VatCertificateFileId, u.IdDocumentFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var docs = await db.Files.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => new FileRef(f.Id, f.OriginalName, f.ContentType), ct);
        FileRef? Doc(Guid? id) => id is not null && docs.TryGetValue(id.Value, out var d) ? d : null;
        return new StoreProfileDto(p.Id, p.FirstName, p.LastName, p.Address, p.SecondMobile, p.CountryId, p.LogoFileId, p.BackgroundFileId, u.Email, u.Phone,
            u.CompanyName, u.CrNumber, u.VatNumber, u.BankName, u.AccountNumber, u.CardNumber, Doc(u.CrDocumentFileId), Doc(u.VatCertificateFileId), Doc(u.IdDocumentFileId));
    }

    /// <summary>Identity always comes from the session. E-mail, password, role, wallet and active flag are not editable here.</summary>
    public async Task<StoreProfileDto> UpdateProfileAsync(Actor actor, StoreProfileInput input, StoreProfileFiles uploads, CancellationToken ct = default)
    {
        var pid = Pid(actor);
        var first = Normalize.Text(input.FirstName);
        if (first is null || first.Length > 200) throw new AppException("profile.name_required");
        if (!await db.Countries.Allowed().AnyAsync(c => c.Id == input.CountryId, ct)) throw new AppException("country.invalid");
        var p = await db.Publishers.Include(x => x.User).SingleAsync(x => x.Id == pid, ct);
        var u = p.User!;
        p.FirstName = first;
        p.LastName = Normalize.Text(input.LastName) ?? "";
        p.Address = Normalize.Text(input.Address);
        p.SecondMobile = Normalize.Text(input.SecondMobile);
        p.CountryId = input.CountryId;
        u.FullName = p.DisplayName;
        u.Phone = JordanPhone.Optional(input.Phone);
        u.CompanyName = Normalize.Text(input.CompanyName);
        u.CrNumber = Normalize.Text(input.CrNumber);
        u.VatNumber = Normalize.Text(input.VatNumber);
        u.BankName = Normalize.Text(input.BankName);
        u.AccountNumber = Normalize.Text(input.AccountNumber);
        u.CardNumber = Normalize.Text(input.CardNumber);
        p.LogoFileId = await files.SaveOptionalAsync(uploads.Logo, FileCategory.PublisherImage, u.Id, p.LogoFileId, ct);
        p.BackgroundFileId = await files.SaveOptionalAsync(uploads.Background, FileCategory.PublisherImage, u.Id, p.BackgroundFileId, ct);
        u.CrDocumentFileId = await files.SaveOptionalAsync(uploads.CrDocument, FileCategory.KycDocument, u.Id, u.CrDocumentFileId, ct);
        u.VatCertificateFileId = await files.SaveOptionalAsync(uploads.VatCertificate, FileCategory.KycDocument, u.Id, u.VatCertificateFileId, ct);
        u.IdDocumentFileId = await files.SaveOptionalAsync(uploads.IdDocument, FileCategory.KycDocument, u.Id, u.IdDocumentFileId, ct);
        audit.Record(actor, "publisher.profile_update", nameof(Publisher), pid);
        await db.SaveChangesAsync(ct);
        return await ProfileAsync(actor, ct);
    }
}

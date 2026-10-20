using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public enum MyBidStatus { Leading, Outbid, Won, Lost }

public sealed record MyBid(long Id, int AuctionId, string RefNumber, string TitleEn, string TitleAr, decimal Amount, DateTime CreatedAtUtc, decimal CurrentPrice, AuctionPhase Phase, MyBidStatus Status);
public sealed record AccountDashboard(int AuctionsAttended, int Won, int Leading, decimal WalletBalance, int UnreadNotifications, int Favorites, int Watching);
public sealed record ProfileDto(int Id, string FullName, string Email, string? Phone, int CountryId, UserRole Role, Guid? AvatarFileId,
    string? CompanyName, string? CrNumber, string? VatNumber, string? BankName, string? AccountNumber, string? CardNumber,
    FileRef? CrDocument, FileRef? VatCertificate, FileRef? IdDocument, decimal WalletBalance, string? AccountHolderName = null);
public sealed record ProfileInput(string FullName, string? Phone, int CountryId, string? CompanyName, string? CrNumber, string? VatNumber,
    string? BankName, string? AccountNumber, string? CardNumber, string? AccountHolderName = null);
public sealed record ProfileFiles(UploadInput? Avatar, UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);
public sealed record NotificationDto(int Id, string Kind, int? AuctionId, string? AuctionTitleEn, string? AuctionTitleAr, decimal? Amount, string? Title, string? Body, string? Url, bool IsRead, DateTime CreatedAtUtc);
public sealed record ReviewInput(int Rating, string? Comment);
public sealed record SellerApplicationInput(string Name, string? Company, string? Mobile, string? Address, int[]? CategoryIds,
    string? CrNumber = null, string? VatNumber = null, string? BankName = null, string? AccountNumber = null, string? CardNumber = null,
    string? AccountHolderName = null, string? AccountType = null);
public sealed record SellerApplicationFiles(UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);
public sealed record SellerApplicationDto(int Id, string Name, string? Company, string? Mobile, string? Address, ApplicationStatus Status, string? RejectionReason,
    DateTime CreatedAtUtc, DateTime? DecidedAtUtc, IReadOnlyList<CategoryRef> Categories, FileRef? CrDocument, FileRef? VatCertificate, FileRef? IdDocument);
/// <summary><c>CanApply</c> is true only for a customer without a seller profile and without a pending application (a rejected one may be re-submitted).</summary>
public sealed record SellerStatus(bool IsSeller, bool CanApply, SellerApplicationDto? Application);

/// <summary>Customer self-service: dashboard, bids, profile, favourites, watch list, notifications, reviews.</summary>
public sealed class AccountService(AppDbContext db, AuctionQueries auctions, FileService files, TimeProvider clock, Audit audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Auction ids where the user holds the highest bid (ties: earliest bid), split by ended/not ended.</summary>
    private IQueryable<int> LeadingAuctionIds(int userId) =>
        db.Auctions.Where(a => db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => (int?)b.UserId).FirstOrDefault() == userId)
            .Select(a => a.Id);

    public async Task<AccountDashboard> DashboardAsync(int userId, CancellationToken ct = default)
    {
        var now = Now;
        var leading = LeadingAuctionIds(userId);
        return new AccountDashboard(
            await db.Bids.Where(b => b.UserId == userId).Select(b => b.AuctionId).Distinct().CountAsync(ct),
            await db.Auctions.CountAsync(a => leading.Contains(a.Id) && (a.EndsAtUtc == null || a.EndsAtUtc <= now), ct),
            await db.Auctions.CountAsync(a => leading.Contains(a.Id) && a.EndsAtUtc > now, ct),
            await db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync(ct),
            await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct),
            await db.Wishlist.CountAsync(w => w.UserId == userId, ct),
            await db.AuctionWatches.CountAsync(w => w.UserId == userId, ct));
    }

    public async Task<PagedResult<MyBid>> BidsAsync(int userId, PageQuery page, CancellationToken ct = default)
    {
        var q = db.Bids.AsNoTracking().Where(b => b.UserId == userId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(b => b.Id).Skip(page.Skip).Take(page.SafeSize).Select(b => new
        {
            b.Id, b.AuctionId, b.Auction!.RefNumber, b.Auction.TitleEn, b.Auction.TitleAr, b.Amount, b.CreatedAtUtc, b.Auction.CurrentPrice,
            b.Auction.StartsAtUtc, b.Auction.EndsAtUtc,
            LeaderBidId = db.Bids.Where(x => x.AuctionId == b.AuctionId).OrderByDescending(x => x.Amount).ThenBy(x => x.Id).Select(x => x.Id).FirstOrDefault(),
            LeaderUserId = db.Bids.Where(x => x.AuctionId == b.AuctionId).OrderByDescending(x => x.Amount).ThenBy(x => x.Id).Select(x => x.UserId).FirstOrDefault(),
        }).ToListAsync(ct);
        var now = Now;
        var items = rows.Select(r =>
        {
            var phase = AuctionRules.Phase(r.StartsAtUtc, r.EndsAtUtc, now);
            var mine = r.LeaderUserId == userId;
            var status = phase == AuctionPhase.Ended ? (mine ? MyBidStatus.Won : MyBidStatus.Lost) : (mine ? MyBidStatus.Leading : MyBidStatus.Outbid);
            return new MyBid(r.Id, r.AuctionId, r.RefNumber, r.TitleEn, r.TitleAr, r.Amount, r.CreatedAtUtc, r.CurrentPrice, phase, status);
        }).ToList();
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>tab = "leading" (not ended, I am the highest bidder) or "won" (ended, I was the highest bidder).</summary>
    public async Task<PagedResult<AuctionCard>> MyAuctionsAsync(int userId, string tab, PageQuery page, CancellationToken ct = default)
    {
        var now = Now;
        var leading = LeadingAuctionIds(userId);
        var q = db.Auctions.AsNoTracking().Where(a => leading.Contains(a.Id));
        q = tab == "won" ? q.Where(a => a.EndsAtUtc == null || a.EndsAtUtc <= now) : q.Where(a => a.EndsAtUtc > now);
        var total = await q.CountAsync(ct);
        var items = await auctions.CardsAsync(q.OrderByDescending(a => a.EndsAtUtc).Skip(page.Skip).Take(page.SafeSize), userId, ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<ProfileDto> ProfileAsync(int userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        var ids = new[] { u.CrDocumentFileId, u.VatCertificateFileId, u.IdDocumentFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var docs = await db.Files.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => new FileRef(f.Id, f.OriginalName, f.ContentType), ct);
        FileRef? Doc(Guid? id) => id is not null && docs.TryGetValue(id.Value, out var d) ? d : null;
        return new ProfileDto(u.Id, u.FullName, u.Email, u.Phone, u.CountryId, u.Role, u.AvatarFileId, u.CompanyName, u.CrNumber, u.VatNumber, u.BankName,
            u.AccountNumber, u.CardNumber, Doc(u.CrDocumentFileId), Doc(u.VatCertificateFileId), Doc(u.IdDocumentFileId), u.WalletBalance, u.AccountHolderName);
    }

    /// <summary>Only these fields are editable by the account owner; e-mail, role, wallet and status are not part of the input at all.</summary>
    public async Task<ProfileDto> UpdateProfileAsync(int userId, ProfileInput input, ProfileFiles uploads, CancellationToken ct = default)
    {
        var name = Normalize.Text(input.FullName);
        if (name is null || name.Length > 200) throw new AppException("profile.name_required");
        if (!await db.Countries.Allowed().AnyAsync(c => c.Id == input.CountryId, ct)) throw new AppException("country.invalid");
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        u.FullName = name;
        u.Phone = JordanPhone.Optional(input.Phone);
        u.CountryId = input.CountryId;
        u.CompanyName = Normalize.Text(input.CompanyName);
        u.CrNumber = Normalize.Text(input.CrNumber);
        u.VatNumber = Normalize.Text(input.VatNumber);
        u.BankName = Normalize.Text(input.BankName);
        u.AccountNumber = Normalize.Text(input.AccountNumber);
        u.CardNumber = Normalize.Text(input.CardNumber);
        u.AccountHolderName = Normalize.Text(input.AccountHolderName);
        u.AvatarFileId = await files.SaveOptionalAsync(uploads.Avatar, FileCategory.UserAvatar, userId, u.AvatarFileId, ct);
        u.CrDocumentFileId = await files.SaveOptionalAsync(uploads.CrDocument, FileCategory.KycDocument, userId, u.CrDocumentFileId, ct);
        u.VatCertificateFileId = await files.SaveOptionalAsync(uploads.VatCertificate, FileCategory.KycDocument, userId, u.VatCertificateFileId, ct);
        u.IdDocumentFileId = await files.SaveOptionalAsync(uploads.IdDocument, FileCategory.KycDocument, userId, u.IdDocumentFileId, ct);
        audit.Record(new Actor(userId, u.Role), "user.profile_update", nameof(User), userId);
        await db.SaveChangesAsync(ct);
        return await ProfileAsync(userId, ct);
    }

    // ---------- becoming a seller ----------

    public const int MaxApplicationsPerDay = 5;

    public async Task<SellerStatus> SellerStatusAsync(Actor actor, CancellationToken ct = default)
    {
        var isSeller = await db.Publishers.AnyAsync(p => p.UserId == actor.UserId, ct);
        var latest = await db.PublisherApplications.AsNoTracking().Where(a => a.UserId == actor.UserId).OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var dto = latest is null ? null : await ApplicationDtoAsync(latest, ct);
        var canApply = actor.Role == UserRole.Customer && !isSeller && latest?.Status != ApplicationStatus.Pending;
        return new SellerStatus(isSeller, canApply, dto);
    }

    private async Task<SellerApplicationDto> ApplicationDtoAsync(PublisherApplication a, CancellationToken ct)
    {
        var ids = new[] { a.CrDocumentFileId, a.VatCertificateFileId, a.IdDocumentFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var docs = await db.Files.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => new FileRef(f.Id, f.OriginalName, f.ContentType), ct);
        FileRef? Doc(Guid? id) => id is not null && docs.TryGetValue(id.Value, out var d) ? d : null;
        var cats = await db.PublisherApplications.AsNoTracking().Where(x => x.Id == a.Id).SelectMany(x => x.Categories).Select(c => new CategoryRef(c.CategoryId, c.Category!.TitleEn, c.Category.TitleAr)).ToListAsync(ct);
        return new SellerApplicationDto(a.Id, a.Name, a.Company, a.Mobile, a.Address, a.Status, a.RejectionReason, a.CreatedAtUtc, a.DecidedAtUtc, cats,
            Doc(a.CrDocumentFileId), Doc(a.VatCertificateFileId), Doc(a.IdDocumentFileId));
    }

    /// <summary>
    /// A signed-in customer applies to publish auctions. Admins and existing sellers cannot apply; one application may be pending at a time
    /// (also enforced by a filtered unique index); the commercial-registration and ID documents are required (uploaded now or already on the profile).
    /// </summary>
    public async Task<int> SubmitSellerApplicationAsync(Actor actor, SellerApplicationInput input, SellerApplicationFiles uploads, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Publisher).SingleAsync(u => u.Id == actor.UserId && u.IsActive && !u.IsDeleted, ct);
        if (user.Role != UserRole.Customer) throw AppException.Forbidden();
        if (user.Publisher is not null) throw AppException.Conflict("application.already_seller");
        if (await db.PublisherApplications.AnyAsync(a => a.UserId == user.Id && a.Status == ApplicationStatus.Pending, ct)) throw AppException.Conflict("application.already_pending");
        var since = Now.AddDays(-1);
        if (await db.PublisherApplications.CountAsync(a => a.UserId == user.Id && a.CreatedAtUtc > since, ct) >= MaxApplicationsPerDay) throw new AppException("application.too_many", 429);

        var name = Normalize.Text(input.Name);
        if (name is null || name.Length > 200) throw new AppException("application.name_required");
        var company = Normalize.Text(input.Company);
        var mobile = JordanPhone.Optional(input.Mobile);
        var address = Normalize.Text(input.Address);
        if (company?.Length > 200 || mobile?.Length > 50 || address?.Length > 1000) throw new AppException("application.field_too_long");
        // Individuals apply with their national ID; businesses with company details and a commercial registration. Both need payout bank details.
        var business = !string.Equals(input.AccountType, "Individual", StringComparison.OrdinalIgnoreCase);
        var crNumber = Normalize.Text(input.CrNumber);
        var vatNumber = Normalize.Text(input.VatNumber);
        var bankName = Normalize.Text(input.BankName);
        var accountNumber = Normalize.Text(input.AccountNumber);
        var holder = Normalize.Text(input.AccountHolderName) ?? company ?? user.FullName;
        if (business && (company is null || crNumber is null)) throw new AppException("application.company_required");
        if (bankName is null || accountNumber is null) throw new AppException("application.bank_required");
        if (crNumber?.Length > 100 || vatNumber?.Length > 100 || bankName.Length > 200 || accountNumber.Length > 100 || holder.Length > 200) throw new AppException("application.field_too_long");
        var categoryIds = (input.CategoryIds ?? []).Distinct().ToArray();
        if (categoryIds.Length > 0 && await db.Categories.CountAsync(c => categoryIds.Contains(c.Id) && c.IsActive, ct) != categoryIds.Length) throw new AppException("category.invalid");
        var hasCr = uploads.CrDocument is not null || user.CrDocumentFileId is not null;
        var hasId = uploads.IdDocument is not null || user.IdDocumentFileId is not null;
        if (business ? !hasCr : !hasId) throw new AppException("application.documents_required");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.CompanyName = business ? company : null; user.CrNumber = business ? crNumber : null; user.VatNumber = business ? vatNumber : null;
        user.BankName = bankName; user.AccountNumber = accountNumber; user.AccountHolderName = holder;
        var app = new PublisherApplication
        {
            UserId = user.Id, Name = name, Company = company, Mobile = mobile ?? user.Phone, Address = address, CountryId = user.CountryId,
            Status = ApplicationStatus.Pending, CreatedAtUtc = Now,
            Categories = categoryIds.Select(id => new PublisherApplicationCategory { CategoryId = id }).ToList(),
        };
        // Documents are private files owned by the applicant (readable by them and by admins only).
        app.CrDocumentFileId = await files.SaveOptionalAsync(uploads.CrDocument, FileCategory.KycDocument, user.Id, user.CrDocumentFileId, ct);
        app.VatCertificateFileId = await files.SaveOptionalAsync(uploads.VatCertificate, FileCategory.KycDocument, user.Id, user.VatCertificateFileId, ct);
        app.IdDocumentFileId = await files.SaveOptionalAsync(uploads.IdDocument, FileCategory.KycDocument, user.Id, user.IdDocumentFileId, ct);
        db.PublisherApplications.Add(app);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw AppException.Conflict("application.already_pending"); // lost a race with a concurrent submission
        }
        audit.Record(actor, "seller_application.submit", nameof(PublisherApplication), app.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return app.Id;
    }

    private async Task EnsureVisibleAsync(int auctionId, CancellationToken ct)
    {
        if (!await auctions.Visible().AnyAsync(a => a.Id == auctionId, ct)) throw AppException.NotFound("auction");
    }

    public async Task SetFavoriteAsync(int userId, int auctionId, bool on, CancellationToken ct = default)
    {
        if (!on)
        {
            await db.Wishlist.Where(w => w.UserId == userId && w.AuctionId == auctionId).ExecuteDeleteAsync(ct);
            return;
        }
        await EnsureVisibleAsync(auctionId, ct);
        if (await db.Wishlist.AnyAsync(w => w.UserId == userId && w.AuctionId == auctionId, ct)) return;
        db.Wishlist.Add(new WishlistItem { UserId = userId, AuctionId = auctionId, CreatedAtUtc = Now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* concurrent duplicate: already in the list, which is the requested state */ }
    }

    public async Task SetWatchAsync(int userId, int auctionId, bool on, CancellationToken ct = default)
    {
        if (!on)
        {
            await db.AuctionWatches.Where(w => w.UserId == userId && w.AuctionId == auctionId).ExecuteDeleteAsync(ct);
            return;
        }
        await EnsureVisibleAsync(auctionId, ct);
        if (await db.AuctionWatches.AnyAsync(w => w.UserId == userId && w.AuctionId == auctionId, ct)) return;
        db.AuctionWatches.Add(new AuctionWatch { UserId = userId, AuctionId = auctionId, CreatedAtUtc = Now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* concurrent duplicate: already watching, which is the requested state */ }
    }

    public async Task<PagedResult<AuctionCard>> FavoritesAsync(int userId, PageQuery page, CancellationToken ct = default)
    {
        var q = auctions.Visible().Where(a => db.Wishlist.Any(w => w.UserId == userId && w.AuctionId == a.Id));
        var total = await q.CountAsync(ct);
        var items = await auctions.CardsAsync(q.OrderBy(a => a.EndsAtUtc).Skip(page.Skip).Take(page.SafeSize), userId, ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<PagedResult<AuctionCard>> WatchingAsync(int userId, PageQuery page, CancellationToken ct = default)
    {
        var q = auctions.Visible().Where(a => db.AuctionWatches.Any(w => w.UserId == userId && w.AuctionId == a.Id));
        var total = await q.CountAsync(ct);
        var items = await auctions.CardsAsync(q.OrderBy(a => a.EndsAtUtc).Skip(page.Skip).Take(page.SafeSize), userId, ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<PagedResult<NotificationDto>> NotificationsAsync(int userId, bool unreadOnly, PageQuery page, CancellationToken ct = default)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.Id).Skip(page.Skip).Take(page.SafeSize).Select(n => new NotificationDto(n.Id, n.Kind, n.AuctionId,
            db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleEn).FirstOrDefault(),
            db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleAr).FirstOrDefault(),
            n.Amount, n.Title, n.Body, n.Url, n.IsRead, n.CreatedAtUtc)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public Task<int> UnreadCountAsync(int userId, CancellationToken ct = default) => db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkReadAsync(int userId, int? id, CancellationToken ct = default)
    {
        var q = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (id is not null)
        {
            if (!await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId, ct)) throw AppException.NotFound("notification");
            q = q.Where(n => n.Id == id);
        }
        await q.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task DeleteNotificationAsync(int userId, int id, CancellationToken ct = default)
    {
        if (await db.Notifications.Where(n => n.Id == id && n.UserId == userId).ExecuteDeleteAsync(ct) == 0) throw AppException.NotFound("notification");
    }

    /// <summary>One review per account per auction; rating 1..5.</summary>
    public async Task<int> AddReviewAsync(int userId, int auctionId, ReviewInput input, CancellationToken ct = default)
    {
        if (input.Rating is < 1 or > 5) throw new AppException("review.invalid_rating");
        var comment = Normalize.Text(input.Comment);
        if (comment is { Length: > 2000 }) throw new AppException("review.comment_too_long");
        await EnsureVisibleAsync(auctionId, ct);
        if (await db.Reviews.AnyAsync(r => r.UserId == userId && r.AuctionId == auctionId, ct)) throw AppException.Conflict("review.already_reviewed");
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync(ct);
        var r = new Review { AuctionId = auctionId, UserId = userId, Rating = input.Rating, Comment = comment, Name = name, CreatedAtUtc = Now };
        db.Reviews.Add(r);
        await db.SaveChangesAsync(ct);
        return r.Id;
    }
}

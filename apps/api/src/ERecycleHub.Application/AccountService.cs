using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public enum MyBidStatus { Leading, Outbid, Won, Lost }

public sealed record MyBid(long Id, int AuctionId, string RefNumber, string TitleEn, string TitleAr, decimal Amount, DateTime CreatedAtUtc, decimal CurrentPrice, AuctionPhase Phase, MyBidStatus Status);
public sealed record AccountDashboard(int AuctionsAttended, int Won, int Leading, decimal WalletBalance, int UnreadNotifications, int Favorites, int Watching);
public sealed record ProfileDto(int Id, string FullName, string Email, string? Phone, int CountryId, UserRole Role, Guid? AvatarFileId,
    string? CompanyName, string? CrNumber, string? VatNumber, string? BankName, string? AccountNumber, string? CardNumber,
    FileRef? CrDocument, FileRef? VatCertificate, FileRef? IdDocument, decimal WalletBalance);
public sealed record ProfileInput(string FullName, string? Phone, int CountryId, string? CompanyName, string? CrNumber, string? VatNumber,
    string? BankName, string? AccountNumber, string? CardNumber);
public sealed record ProfileFiles(UploadInput? Avatar, UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);
public sealed record NotificationDto(int Id, string Kind, int? AuctionId, string? AuctionTitleEn, string? AuctionTitleAr, decimal? Amount, string? Title, string? Body, string? Url, bool IsRead, DateTime CreatedAtUtc);
public sealed record ReviewInput(int Rating, string? Comment);

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
            u.AccountNumber, u.CardNumber, Doc(u.CrDocumentFileId), Doc(u.VatCertificateFileId), Doc(u.IdDocumentFileId), u.WalletBalance);
    }

    /// <summary>Only these fields are editable by the account owner; e-mail, role, wallet and status are not part of the input at all.</summary>
    public async Task<ProfileDto> UpdateProfileAsync(int userId, ProfileInput input, ProfileFiles uploads, CancellationToken ct = default)
    {
        var name = Normalize.Text(input.FullName);
        if (name is null || name.Length > 200) throw new AppException("profile.name_required");
        if (!await db.Countries.AnyAsync(c => c.Id == input.CountryId, ct)) throw new AppException("country.invalid");
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        u.FullName = name;
        u.Phone = Normalize.Text(input.Phone);
        u.CountryId = input.CountryId;
        u.CompanyName = Normalize.Text(input.CompanyName);
        u.CrNumber = Normalize.Text(input.CrNumber);
        u.VatNumber = Normalize.Text(input.VatNumber);
        u.BankName = Normalize.Text(input.BankName);
        u.AccountNumber = Normalize.Text(input.AccountNumber);
        u.CardNumber = Normalize.Text(input.CardNumber);
        u.AvatarFileId = await files.SaveOptionalAsync(uploads.Avatar, FileCategory.UserAvatar, userId, u.AvatarFileId, ct);
        u.CrDocumentFileId = await files.SaveOptionalAsync(uploads.CrDocument, FileCategory.KycDocument, userId, u.CrDocumentFileId, ct);
        u.VatCertificateFileId = await files.SaveOptionalAsync(uploads.VatCertificate, FileCategory.KycDocument, userId, u.VatCertificateFileId, ct);
        u.IdDocumentFileId = await files.SaveOptionalAsync(uploads.IdDocument, FileCategory.KycDocument, userId, u.IdDocumentFileId, ct);
        audit.Record(new Actor(userId, u.Role), "user.profile_update", nameof(User), userId);
        await db.SaveChangesAsync(ct);
        return await ProfileAsync(userId, ct);
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

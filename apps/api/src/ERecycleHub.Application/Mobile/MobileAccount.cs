using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application.Mobile;

/// <summary>Signed-in user's data for the mobile API: profile, my auctions, favourites, wallet, notifications.</summary>
public sealed class MobileAccount(AppDbContext db, AuctionQueries auctions, MobileCatalog catalog, FileService files, SessionService sessions,
    IFileUrls urls, TimeProvider clock, Audit audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>A 422 VALIDATION_FAILED on one field. <paramref name="messageKey"/> is translated by the API (ErrorMessages), e.g. "email.taken".</summary>
    public static AppException Invalid(string field, string messageKey) =>
        new("validation.failed", 422, new Dictionary<string, string[]> { [field] = [messageKey] });

    // ---------- profile ----------

    public async Task<UserProfileDto> ProfileAsync(int userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.Id, x.FullName, x.Email, x.Phone, x.AvatarFileId }).SingleAsync(ct);
        return new UserProfileDto(u.Id, u.FullName, u.Email, MobilePhone.ToE164(u.Phone), urls.Of(u.AvatarFileId));
    }

    public async Task<UserProfileDto> UpdateProfileAsync(int userId, string? fullName, string? email, string? phone, CancellationToken ct = default)
    {
        var name = Normalize.Text(fullName);
        if (name is null || name.Length > 200) throw Invalid("fullName", "name.required");
        var mail = Normalize.Text(email) is { } e ? Normalize.Email(e) : null;
        if (mail is null || mail.Length > 256 || !System.Text.RegularExpressions.Regex.IsMatch(mail, @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$"))
            throw Invalid("email", "email.invalid");
        string? local = null;
        if (Normalize.Text(phone) is { } p && (local = MobilePhone.ToLocal(p)) is null) throw Invalid("phone", "phone.invalid");

        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (mail != u.Email && await db.Users.AnyAsync(x => x.Email == mail && x.Id != userId, ct)) throw Invalid("email", "email.taken");
        if (local is not null && local != u.Phone && await db.Users.AnyAsync(x => x.Phone == local && x.Id != userId && !x.IsDeleted, ct))
            throw Invalid("phone", "phone.taken");
        u.FullName = name;
        u.Email = mail;
        u.Phone = local;
        audit.Record(new Actor(userId, u.Role), "user.profile_update", nameof(User), userId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw Invalid("email", "email.taken"); }
        return await ProfileAsync(userId, ct);
    }

    public async Task<ProfileImageDto> SetImageAsync(int userId, UploadInput file, CancellationToken ct = default)
    {
        var stored = await files.SaveAsync(file, FileCategory.UserAvatar, userId, ct);
        await db.SaveChangesAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.AvatarFileId, stored.Id), ct);
        return new ProfileImageDto(urls.Of(stored.Id));
    }

    /// <summary>
    /// Store-policy account deletion: personal data is removed and the e-mail freed; bids and wallet history stay (anonymised) for the
    /// auction and financial record. Every session and push token ends.
    /// </summary>
    public async Task DeleteAccountAsync(int userId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        u.IsDeleted = true;
        u.IsActive = false;
        u.FullName = "Deleted user";
        u.Email = $"deleted-{u.Id}-{Guid.NewGuid():N}"[..40] + "@deleted.invalid";
        u.Phone = null;
        u.PasswordHash = null;
        u.AvatarFileId = null;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        u.CompanyName = u.CrNumber = u.VatNumber = u.BankName = u.CardNumber = u.AccountNumber = u.AccountHolderName = null;
        u.IsFeaturedClient = false;
        await db.Wishlist.Where(w => w.UserId == userId).ExecuteDeleteAsync(ct);
        await db.AuctionWatches.Where(w => w.UserId == userId).ExecuteDeleteAsync(ct);
        audit.Record(new Actor(userId, u.Role), "user.delete_self", nameof(User), userId);
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(userId, null, ct);
        await tx.CommitAsync(ct);
    }

    // ---------- auctions ----------

    /// <summary>live: running auctions the user has bid on. won: ended auctions where the user holds the highest bid (ties: earliest).</summary>
    public async Task<MyAuctionsDto> MyAuctionsAsync(int userId, CancellationToken ct = default)
    {
        var now = Now;
        var mine = auctions.Visible().Where(a => db.Bids.Any(b => b.AuctionId == a.Id && b.UserId == userId));
        var live = mine.Where(a => a.StartsAtUtc <= now && a.EndsAtUtc > now);
        var won = mine.Where(a => a.StartsAtUtc <= now && (a.EndsAtUtc == null || a.EndsAtUtc <= now)
            && db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => b.UserId).FirstOrDefault() == userId);
        return new MyAuctionsDto(await catalog.ItemsAsync(live, ct), await catalog.ItemsAsync(won, ct));
    }

    public Task<List<AuctionItemDto>> FavoritesAsync(int userId, CancellationToken ct = default) =>
        catalog.ItemsAsync(auctions.Visible().Where(a => db.Wishlist.Any(w => w.UserId == userId && w.AuctionId == a.Id)), ct);

    // ---------- wallet ----------

    public async Task<WalletDto> WalletAsync(int userId, CancellationToken ct = default)
    {
        var balance = await db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync(ct);
        var rows = await db.WalletTransactions.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Type, t.Amount, t.Status, t.CreatedAtUtc }).ToListAsync(ct);
        // Enum strings the app understands: type Deposit = money in, anything else = out; status Completed / Pending / anything else = rejected.
        return new WalletDto(balance, rows.Select(t => new WalletTransactionItemDto(t.Id,
            t.Type == WalletTransactionType.Withdrawal ? "Withdraw" : "Deposit", t.Amount,
            t.Status switch { WalletTransactionStatus.Approved => "Completed", WalletTransactionStatus.Pending => "Pending", WalletTransactionStatus.Reversed => "Reversed", _ => "Rejected" },
            t.CreatedAtUtc)).ToList());
    }

    // ---------- notifications ----------

    public static readonly TimeSpan RestoreWindow = TimeSpan.FromDays(1);

    public async Task<List<AppNotificationDto>> NotificationsAsync(int userId, CancellationToken ct = default)
    {
        var rows = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.DeletedAtUtc == null).OrderByDescending(n => n.Id).Take(500)
            .Select(n => new
            {
                n.Id, n.Kind, n.Amount, n.Title, n.Body, n.IsRead, n.CreatedAtUtc,
                AuctionEn = db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleEn).FirstOrDefault(),
                AuctionAr = db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleAr).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(n =>
        {
            var t = NotificationTexts.For(n.Kind, n.AuctionEn, n.AuctionAr, n.Amount, n.Title, n.Body);
            return new AppNotificationDto(n.Id, NotificationTexts.TypeOf(n.Kind), t.TitleEn, t.TitleAr, t.BodyEn, t.BodyAr, n.CreatedAtUtc, n.IsRead);
        }).ToList();
    }

    public async Task MarkReadAsync(int userId, int[]? ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Length == 0) return;
        if (ids.Length > 1000) throw Invalid("ids", "ids.too_many");
        await db.Notifications.Where(n => n.UserId == userId && ids.Contains(n.Id) && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task DeleteNotificationAsync(int userId, int id, CancellationToken ct = default)
    {
        var now = Now;
        if (await db.Notifications.Where(n => n.Id == id && n.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAtUtc, n => n.DeletedAtUtc ?? now), ct) == 0)
            throw AppException.NotFound("notification");
    }

    /// <summary>Undo of a delete, possible for <see cref="RestoreWindow"/>. Restoring one that is not deleted succeeds (idempotent).</summary>
    public async Task RestoreNotificationAsync(int userId, int id, CancellationToken ct = default)
    {
        var since = Now - RestoreWindow;
        var n = await db.Notifications.Where(x => x.Id == id && x.UserId == userId).Select(x => new { x.DeletedAtUtc }).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("notification");
        if (n.DeletedAtUtc is null) return;
        if (n.DeletedAtUtc < since) throw AppException.NotFound("notification");
        await db.Notifications.Where(x => x.Id == id && x.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAtUtc, (DateTime?)null), ct);
    }
}

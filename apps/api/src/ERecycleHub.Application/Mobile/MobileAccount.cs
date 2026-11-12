using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application.Mobile;

/// <summary>Signed-in user's data for the mobile API: profile, my auctions, favourites, wallet, notifications.</summary>
public sealed class MobileAccount(AppDbContext db, AuctionQueries auctions, MobileCatalog catalog, FileService files, SessionService sessions,
    IFileUrls urls, TimeProvider clock, Audit audit, BiddingService bidding, WalletService walletService)
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
        // A seller's auctions stop being biddable with the account: lock them before the user row, return the leaders' held money afterwards.
        var sellerAuctions = await bidding.LockSellerAuctionsAsync(userId, ct);
        // Then the user's wallet row (auction rows first, wallet rows second, as everywhere): nothing can bid, withdraw or hold while the check runs.
        await db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}").ToListAsync(ct);
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        // Money must be settled first: an account with a balance, a pending withdrawal or money held by a leading bid cannot be deleted.
        var pending = await walletService.PendingWithdrawalsAsync(userId, ct);
        var held = await walletService.HeldAsync(userId, ct);
        if (u.WalletBalance > 0 || pending > 0 || held > 0)
        {
            var role = u.Role;
            var balance = u.WalletBalance;
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            audit.Record(new Actor(userId, role), "user.delete_refused", nameof(User), userId, new { balance, pendingWithdrawals = pending, held });
            await db.SaveChangesAsync(ct);
            throw AppException.Conflict("account.delete_blocked", new { balance, pendingWithdrawals = pending, held });
        }
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
        if (sellerAuctions.Count > 0) await bidding.ReleaseHoldsAsync(sellerAuctions, userId, ct);
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(userId, null, ct);
        await tx.CommitAsync(ct);
    }

    // ---------- auctions ----------

    /// <summary>live: running auctions the user has bid on. won: ended auctions where the user holds the highest bid (ties: earliest).</summary>
    public sealed record MyAuctionsPage(MyAuctionsDto Data, int LiveTotal, int WonTotal, int ExpiredTotal);

    /// <summary>live: running auctions the user has bid on. won: ended auctions where the user holds the highest bid (ties: earliest). expired: ended auctions the user bid on and did not win. Ended auctions stay here forever. Each list is one page.</summary>
    public async Task<MyAuctionsPage> MyAuctionsAsync(int userId, int page, int size, CancellationToken ct = default)
    {
        var now = Now;
        var mine = auctions.Visible().Where(a => db.Bids.Any(b => b.AuctionId == a.Id && b.UserId == userId));
        var live = mine.Where(a => a.StartsAtUtc <= now && a.EndsAtUtc > now);
        var endedMine = mine.Where(a => a.StartsAtUtc <= now && (a.EndsAtUtc == null || a.EndsAtUtc <= now));
        var won = endedMine.Where(a => db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => b.UserId).FirstOrDefault() == userId);
        var expired = endedMine.Where(a => db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => b.UserId).FirstOrDefault() != userId);
        var l = await catalog.ItemsPageAsync(live, page, size, ct);
        var w = await catalog.ItemsPageAsync(won, page, size, ct);
        var x = await catalog.ItemsPageAsync(expired, page, size, ct);
        return new MyAuctionsPage(new MyAuctionsDto(l.Items, w.Items, x.Items), l.Total, w.Total, x.Total);
    }

    public Task<MobileList<AuctionItemDto>> FavoritesAsync(int userId, int page, int size, CancellationToken ct = default) =>
        catalog.ItemsPageAsync(auctions.Visible().Where(a => db.Wishlist.Any(w => w.UserId == userId && w.AuctionId == a.Id)), page, size, ct);

    // ---------- wallet ----------

    /// <summary>Most recent bids of the user shown as hold lines in the wallet history.</summary>
    private const int MaxHoldLines = 200;

    public async Task<WalletDto> WalletAsync(int userId, CancellationToken ct = default)
    {
        var balance = await walletService.ShownBalanceAsync(userId, ct);
        var rows = await db.WalletTransactions.AsNoTracking().Where(t => t.UserId == userId).OrderByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Type, t.Amount, t.Status, t.CreatedAtUtc }).ToListAsync(ct);
        // Enum strings the app understands: type Deposit = money in, anything else = out; status Completed / Pending / anything else = rejected.
        var lines = rows.Select(t => (Date: t.CreatedAtUtc, Order: (long)t.Id, Item: new WalletTransactionItemDto(t.Id,
            t.Type == WalletTransactionType.Withdrawal ? "Withdraw" : "Deposit", t.Amount,
            t.Status switch { WalletTransactionStatus.Approved => "Completed", WalletTransactionStatus.Pending => "Pending", WalletTransactionStatus.Reversed => "Reversed", _ => "Rejected" },
            t.CreatedAtUtc))).ToList();
        lines.AddRange(await HoldLinesAsync(userId, ct));
        return new WalletDto(balance, lines.OrderByDescending(l => l.Date).ThenByDescending(l => l.Order).Select(l => l.Item).ToList());
    }

    /// <summary>
    /// Display-only history lines derived from the user's own bids (reads only, nothing is stored and no balance or hold changes): "Hold" when a bid reserved money
    /// (Pending while it still holds, Completed once the auction closed and it was paid) and "HoldRelease" when that hold was let go (outbid, raised, or the sale was voided).
    /// Ids are negative: bid id x 2 for the hold, x 2 + 1 for the release.
    /// </summary>
    private async Task<List<(DateTime Date, long Order, WalletTransactionItemDto Item)>> HoldLinesAsync(int userId, CancellationToken ct)
    {
        var bids = await db.Bids.AsNoTracking().Where(b => b.UserId == userId && b.HeldAmount > 0).OrderByDescending(b => b.Id).Take(MaxHoldLines)
            .Select(b => new { b.Id, b.AuctionId, b.HeldAmount, b.CreatedAtUtc, b.ReleasedAtUtc, b.SettledAtUtc, TitleEn = b.Auction!.TitleEn, TitleAr = b.Auction!.TitleAr })
            .ToListAsync(ct);
        var lines = new List<(DateTime, long, WalletTransactionItemDto)>();
        foreach (var b in bids)
        {
            var status = b.SettledAtUtc is not null ? "Completed" : "Pending";
            lines.Add((b.CreatedAtUtc, b.Id * 2, new WalletTransactionItemDto(checked((int)(-b.Id * 2)), "Hold", b.HeldAmount, status, b.CreatedAtUtc, b.AuctionId, b.TitleEn, b.TitleAr)));
            if (b.ReleasedAtUtc is { } released && b.SettledAtUtc is null)
                lines.Add((released, b.Id * 2 + 1, new WalletTransactionItemDto(checked((int)(-b.Id * 2 - 1)), "HoldRelease", b.HeldAmount, "Completed", released, b.AuctionId, b.TitleEn, b.TitleAr)));
        }
        return lines;
    }

    // ---------- notifications ----------

    public static readonly TimeSpan RestoreWindow = TimeSpan.FromDays(1);

    /// <summary>Newest first, one page (default and maximum 100) with the total over all pages. <paramref name="unreadOnly"/> narrows it.</summary>
    public async Task<MobileList<AppNotificationDto>> NotificationsAsync(int userId, bool unreadOnly, int page, int size, CancellationToken ct = default)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.DeletedAtUtc == null);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.Id).Skip((page - 1) * size).Take(size)
            .Select(n => new
            {
                n.Id, n.Kind, n.Amount, n.Title, n.Body, n.IsRead, n.CreatedAtUtc, n.AuctionId,
                AuctionEn = db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleEn).FirstOrDefault(),
                AuctionAr = db.Auctions.Where(a => a.Id == n.AuctionId).Select(a => a.TitleAr).FirstOrDefault(),
            }).ToListAsync(ct);
        var items = rows.Select(n =>
        {
            var t = NotificationTexts.For(n.Kind, n.AuctionEn, n.AuctionAr, n.Amount, n.Title, n.Body);
            return new AppNotificationDto(n.Id, NotificationTexts.TypeOf(n.Kind), t.TitleEn, t.TitleAr, t.BodyEn, t.BodyAr, n.CreatedAtUtc, n.IsRead, n.Kind, n.AuctionId, n.Amount);   // kind = the exact event (auction.outbid), docs/MOBILE-API.md section 5
        }).ToList();
        return new MobileList<AppNotificationDto>(items, total);
    }

    public Task<int> UnreadCountAsync(int userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.DeletedAtUtc == null && !n.IsRead, ct);

    public Task MarkAllReadAsync(int userId, CancellationToken ct = default) =>
        db.Notifications.Where(n => n.UserId == userId && n.DeletedAtUtc == null && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

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

using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

public sealed record UserRow(int Id, string FullName, string Email, string? Phone, string? CountryNameEn, string? CountryNameAr, decimal WalletBalance,
    bool IsActive, bool IsFeaturedClient, int BidCount, DateTime CreatedAtUtc, Guid? AvatarFileId);
public sealed record AccountFieldsInput(string FullName, string Email, string? Phone, int CountryId, string? Password, bool IsActive,
    string? CompanyName, string? CrNumber, string? VatNumber, string? BankName, string? AccountNumber, string? CardNumber);
public sealed record AdminUserDetails(ProfileDto Profile, bool IsActive, bool IsFeaturedClient, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc,
    int BidCount, int Won, IReadOnlyList<MyBid> RecentBids);
public sealed record PublisherRow(int Id, int UserId, string Name, string Email, string? Phone, string? Address, string? CountryNameEn, string? CountryNameAr,
    int AuctionCount, bool IsActive, Guid? LogoFileId, DateTime CreatedAtUtc);
public sealed record PublisherInput(string FirstName, string? LastName, string? Address, string? SecondMobile, AccountFieldsInput Account);
public sealed record PublisherFiles(UploadInput? Logo, UploadInput? Background, UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);
public sealed record AdminPublisherDetails(PublisherRow Publisher, StoreProfileDto Profile, decimal WalletBalance, int BidsOnAuctions, int Bidders);
public sealed record ApplicationRow(int Id, string Name, string Email, string? Mobile, string? Company, string? CountryNameEn, string? CountryNameAr,
    ApplicationStatus Status, DateTime CreatedAtUtc, DateTime? DecidedAtUtc, IReadOnlyList<CategoryRef> Categories);
public sealed record ApprovalResult(int UserId, int PublisherId, bool EmailSent, string? TemporaryPassword);

/// <summary>Administrator management of customers, publishers and publisher applications.</summary>
public sealed class AdminPeople(AppDbContext db, PasswordService passwords, FileService files, WalletService wallet, AccountService accounts,
    IEmailSender mailer, TimeProvider clock, Audit audit, ILogger<AdminPeople> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- customers ----------

    public async Task<PagedResult<UserRow>> UsersAsync(bool active, string? q, PageQuery page, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Customer && !u.IsDeleted && u.IsActive == active);
        if (Normalize.Text(q) is { } term) users = users.Where(u => u.FullName.Contains(term) || u.Email.Contains(term) || (u.Phone != null && u.Phone.Contains(term)));
        var total = await users.CountAsync(ct);
        var items = await users.OrderByDescending(u => u.Id).Skip(page.Skip).Take(page.SafeSize).Select(u => new UserRow(u.Id, u.FullName, u.Email, u.Phone,
            u.Country!.NameEn, u.Country.NameAr, u.WalletBalance, u.IsActive, u.IsFeaturedClient, db.Bids.Count(b => b.UserId == u.Id), u.CreatedAtUtc, u.AvatarFileId)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>Lightweight picker for forms (e.g. adding a wallet credit).</summary>
    public Task<List<UserRow>> SearchAccountsAsync(string? q, CancellationToken ct = default)
    {
        var term = Normalize.Text(q) ?? "";
        return db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role != UserRole.Admin && (term == "" || u.FullName.Contains(term) || u.Email.Contains(term)))
            .OrderBy(u => u.FullName).Take(20).Select(u => new UserRow(u.Id, u.FullName, u.Email, u.Phone, null, null, u.WalletBalance, u.IsActive, u.IsFeaturedClient, 0, u.CreatedAtUtc, u.AvatarFileId))
            .ToListAsync(ct);
    }

    public async Task<AdminUserDetails> UserAsync(int id, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == id && x.Role == UserRole.Customer && !x.IsDeleted)
            .Select(x => new { x.IsActive, x.IsFeaturedClient, x.CreatedAtUtc, x.LastLoginAtUtc }).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("user");
        var dash = await accounts.DashboardAsync(id, ct);
        return new AdminUserDetails(await accounts.ProfileAsync(id, ct), u.IsActive, u.IsFeaturedClient, u.CreatedAtUtc, u.LastLoginAtUtc,
            await db.Bids.CountAsync(b => b.UserId == id, ct), dash.Won, (await accounts.BidsAsync(id, new PageQuery(1, 20), ct)).Items);
    }

    public async Task<int> CreateUserAsync(Actor admin, AccountFieldsInput input, ProfileFiles uploads, decimal openingCredit, CancellationToken ct = default)
    {
        if (openingCredit != 0 && !Money.IsValidPositive(openingCredit, WalletService.MaxTopUp)) throw new AppException("wallet.invalid_amount");
        var user = await NewAccountAsync(input, UserRole.Customer, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await ApplyKycFilesAsync(user, uploads.Avatar, uploads.CrDocument, uploads.VatCertificate, uploads.IdDocument, ct);
        audit.Record(admin, "user.create", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Legacy let the admin type a starting wallet value; here it becomes an approved credit so it appears in the ledger.
        if (openingCredit > 0) await wallet.CreateApprovedCreditAsync(admin, user.Id, openingCredit, null, null, "Opening balance", ct);
        return user.Id;
    }

    public async Task UpdateUserAsync(Actor admin, int id, AccountFieldsInput input, ProfileFiles uploads, CancellationToken ct = default)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.Role == UserRole.Customer && !x.IsDeleted, ct) ?? throw AppException.NotFound("user");
        await ApplyAccountFieldsAsync(u, input, ct);
        await ApplyKycFilesAsync(u, uploads.Avatar, uploads.CrDocument, uploads.VatCertificate, uploads.IdDocument, ct);
        audit.Record(admin, "user.update", nameof(User), id);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetUserActiveAsync(Actor admin, int id, bool active, CancellationToken ct = default)
    {
        if (id == admin.UserId) throw new AppException("user.cannot_deactivate_self");
        var newStamp = Guid.NewGuid().ToString("N");
        var rows = await db.Users.Where(u => u.Id == id && u.Role != UserRole.Admin && !u.IsDeleted).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.IsActive, active)
            .SetProperty(u => u.SecurityStamp, u => active ? u.SecurityStamp : newStamp), ct);
        if (rows == 0) throw AppException.NotFound("user");
        audit.Record(admin, active ? "user.activate" : "user.deactivate", nameof(User), id);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetFeaturedAsync(Actor admin, int id, bool featured, CancellationToken ct = default)
    {
        if (await db.Users.Where(u => u.Id == id && u.Role == UserRole.Customer).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsFeaturedClient, featured), ct) == 0)
            throw AppException.NotFound("user");
        audit.Record(admin, "user.featured", nameof(User), id, new { featured });
        await db.SaveChangesAsync(ct);
    }

    // ---------- publishers ----------

    public async Task<PagedResult<PublisherRow>> PublishersAsync(bool active, string? q, PageQuery page, CancellationToken ct = default)
    {
        var pubs = db.Publishers.AsNoTracking().Where(p => p.User!.IsActive == active && !p.User.IsDeleted);
        if (Normalize.Text(q) is { } term) pubs = pubs.Where(p => p.FirstName.Contains(term) || p.LastName.Contains(term) || p.User!.Email.Contains(term));
        var total = await pubs.CountAsync(ct);
        var items = await PublisherRows(pubs.OrderByDescending(p => p.Id)).Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public Task<List<CategoryRef>> PublisherOptionsAsync(CancellationToken ct = default) =>
        db.Publishers.AsNoTracking().OrderBy(p => p.FirstName).Select(p => new CategoryRef(p.Id, p.FirstName + (p.LastName == "" ? "" : " " + p.LastName), p.FirstName + (p.LastName == "" ? "" : " " + p.LastName))).ToListAsync(ct);

    private IQueryable<PublisherRow> PublisherRows(IQueryable<Publisher> q) => q.Select(p => new PublisherRow(p.Id, p.UserId,
        p.FirstName + (p.LastName == "" ? "" : " " + p.LastName), p.User!.Email, p.User.Phone, p.Address, p.Country!.NameEn, p.Country.NameAr,
        db.Auctions.Count(a => a.PublisherId == p.Id), p.User.IsActive, p.LogoFileId, p.CreatedAtUtc));

    public async Task<AdminPublisherDetails> PublisherAsync(int id, SellerService seller, CancellationToken ct = default)
    {
        var row = await PublisherRows(db.Publishers.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("publisher");
        var actor = new Actor(row.UserId, UserRole.Publisher, id);
        var profile = await seller.ProfileAsync(actor, ct);
        var balance = await db.Users.Where(u => u.Id == row.UserId).Select(u => u.WalletBalance).SingleAsync(ct);
        var bids = db.Bids.Where(b => b.Auction!.PublisherId == id);
        return new AdminPublisherDetails(row, profile, balance, await bids.CountAsync(ct), await bids.Select(b => b.UserId).Distinct().CountAsync(ct));
    }

    public async Task<int> CreatePublisherAsync(Actor admin, PublisherInput input, PublisherFiles uploads, CancellationToken ct = default)
    {
        var first = Normalize.Text(input.FirstName) ?? throw new AppException("profile.name_required");
        var user = await NewAccountAsync(input.Account with { FullName = (first + " " + (input.LastName ?? "")).Trim() }, UserRole.Publisher, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var p = new Publisher
        {
            UserId = user.Id, CountryId = user.CountryId, FirstName = first, LastName = Normalize.Text(input.LastName) ?? "",
            Address = Normalize.Text(input.Address), SecondMobile = Normalize.Text(input.SecondMobile), CreatedAtUtc = Now,
        };
        db.Publishers.Add(p);
        await ApplyKycFilesAsync(user, null, uploads.CrDocument, uploads.VatCertificate, uploads.IdDocument, ct);
        p.LogoFileId = await files.SaveOptionalAsync(uploads.Logo, FileCategory.PublisherImage, user.Id, null, ct);
        p.BackgroundFileId = await files.SaveOptionalAsync(uploads.Background, FileCategory.PublisherImage, user.Id, null, ct);
        await db.SaveChangesAsync(ct);
        audit.Record(admin, "publisher.create", nameof(Publisher), p.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return p.Id;
    }

    public async Task UpdatePublisherAsync(Actor admin, int id, PublisherInput input, PublisherFiles uploads, CancellationToken ct = default)
    {
        var p = await db.Publishers.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("publisher");
        var first = Normalize.Text(input.FirstName) ?? throw new AppException("profile.name_required");
        p.FirstName = first;
        p.LastName = Normalize.Text(input.LastName) ?? "";
        p.Address = Normalize.Text(input.Address);
        p.SecondMobile = Normalize.Text(input.SecondMobile);
        p.CountryId = input.Account.CountryId;
        await ApplyAccountFieldsAsync(p.User!, input.Account with { FullName = p.DisplayName }, ct);
        await ApplyKycFilesAsync(p.User!, null, uploads.CrDocument, uploads.VatCertificate, uploads.IdDocument, ct);
        p.LogoFileId = await files.SaveOptionalAsync(uploads.Logo, FileCategory.PublisherImage, p.UserId, p.LogoFileId, ct);
        p.BackgroundFileId = await files.SaveOptionalAsync(uploads.Background, FileCategory.PublisherImage, p.UserId, p.BackgroundFileId, ct);
        audit.Record(admin, "publisher.update", nameof(Publisher), id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deactivating a publisher hides its auctions from the public (via the visibility filter); auctions themselves are untouched (legacy flipped them).</summary>
    public async Task SetPublisherActiveAsync(Actor admin, int id, bool active, CancellationToken ct = default)
    {
        var userId = await db.Publishers.Where(p => p.Id == id).Select(p => (int?)p.UserId).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("publisher");
        await SetUserActiveAsync(admin, userId, active, ct);
    }

    /// <summary>Only a publisher with no auctions and no wallet history can be deleted; otherwise deactivate it.</summary>
    public async Task DeletePublisherAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var p = await db.Publishers.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("publisher");
        var uid = p.UserId;
        if (await db.Auctions.AnyAsync(a => a.PublisherId == id, ct) || await db.Bids.AnyAsync(b => b.UserId == uid, ct)
            || await db.WalletTransactions.AnyAsync(t => t.UserId == uid, ct) || await db.WalletLedger.AnyAsync(l => l.UserId == uid, ct))
            throw AppException.Conflict("publisher.in_use");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Notifications.Where(n => n.UserId == uid).ExecuteDeleteAsync(ct);
        await db.Wishlist.Where(w => w.UserId == uid).ExecuteDeleteAsync(ct);
        await db.AuctionWatches.Where(w => w.UserId == uid).ExecuteDeleteAsync(ct);
        await db.Reviews.Where(r => r.UserId == uid).ExecuteDeleteAsync(ct);
        await db.PasswordResetTokens.Where(t => t.UserId == uid).ExecuteDeleteAsync(ct);
        await db.Files.Where(f => f.OwnerUserId == uid).ExecuteUpdateAsync(s => s.SetProperty(f => f.OwnerUserId, (int?)null), ct);
        db.Publishers.Remove(p);
        await db.SaveChangesAsync(ct);
        // The user row is soft-deleted: its e-mail stays reserved and audit rows keep resolving.
        await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDeleted, true).SetProperty(u => u.IsActive, false)
            .SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString("N")), ct);
        audit.Record(admin, "publisher.delete", nameof(Publisher), id, new { uid });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- applications ----------

    public async Task<PagedResult<ApplicationRow>> ApplicationsAsync(ApplicationStatus? status, PageQuery page, CancellationToken ct = default)
    {
        var q = db.PublisherApplications.AsNoTracking();
        if (status is not null) q = q.Where(a => a.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.SafeSize).Select(a => new ApplicationRow(a.Id, a.Name, a.Email, a.Mobile, a.Company,
            db.Countries.Where(c => c.Id == a.CountryId).Select(c => c.NameEn).FirstOrDefault(), db.Countries.Where(c => c.Id == a.CountryId).Select(c => c.NameAr).FirstOrDefault(),
            a.Status, a.CreatedAtUtc, a.DecidedAtUtc,
            a.Categories.Select(c => new CategoryRef(c.CategoryId, c.Category!.TitleEn, c.Category.TitleAr)).ToList())).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    /// <summary>
    /// Creates a publisher account (role Publisher — never Admin) with the password the applicant chose, or a random one that is e-mailed.
    /// The status change is a conditional UPDATE, so a double click or replay cannot create two accounts.
    /// </summary>
    public async Task<ApprovalResult> ApproveAsync(Actor admin, int id, string loginUrl, bool arabic, CancellationToken ct = default)
    {
        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.PublisherApplications.Where(a => a.Id == id && a.Status == ApplicationStatus.Pending).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, ApplicationStatus.Approved).SetProperty(a => a.DecidedAtUtc, now).SetProperty(a => a.DecidedByUserId, admin.UserId), ct);
        if (claimed == 0)
        {
            if (!await db.PublisherApplications.AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound("application");
            throw AppException.Conflict("application.not_pending");
        }
        var app = await db.PublisherApplications.SingleAsync(a => a.Id == id, ct);
        if (await db.Users.AnyAsync(u => u.Email == app.Email, ct)) throw AppException.Conflict("auth.email_taken");

        var countryId = app.CountryId ?? await db.Countries.Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
            ?? throw new AppException("country.invalid");
        var temp = app.PasswordHash is null ? PasswordService.RandomPassword() : null;
        var user = new User
        {
            FullName = app.Name, Email = app.Email, Phone = app.Mobile, CountryId = countryId, Role = UserRole.Publisher, CompanyName = app.Company,
            PasswordHash = app.PasswordHash ?? passwords.Hash(temp!), CreatedAtUtc = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var pub = new Publisher { UserId = user.Id, CountryId = countryId, FirstName = app.Name, CreatedAtUtc = now };
        db.Publishers.Add(pub);
        app.PasswordHash = null;
        app.CreatedUserId = user.Id;
        audit.Record(admin, "application.approve", nameof(PublisherApplication), id, new { userId = user.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var sent = true;
        try
        {
            var (subject, html) = EmailTemplates.ApplicationApproved(arabic, app.Email, temp, loginUrl);
            await mailer.SendAsync(app.Email, subject, html, ct);
        }
        catch (Exception ex)
        {
            sent = false;
            log.LogError(ex, "Approval e-mail for application {Id} failed", id);
        }
        // If the e-mail failed, the one-time password is handed to the admin once so it is not lost (legacy behaviour).
        return new ApprovalResult(user.Id, pub.Id, sent, sent ? null : temp);
    }

    public async Task RejectAsync(Actor admin, int id, bool arabic, CancellationToken ct = default)
    {
        var now = Now;
        var rows = await db.PublisherApplications.Where(a => a.Id == id && a.Status == ApplicationStatus.Pending).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, ApplicationStatus.Rejected).SetProperty(a => a.DecidedAtUtc, now).SetProperty(a => a.DecidedByUserId, admin.UserId)
            .SetProperty(a => a.PasswordHash, (string?)null), ct);
        if (rows == 0)
        {
            if (!await db.PublisherApplications.AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound("application");
            throw AppException.Conflict("application.not_pending");
        }
        audit.Record(admin, "application.reject", nameof(PublisherApplication), id);
        await db.SaveChangesAsync(ct);
        var email = await db.PublisherApplications.Where(a => a.Id == id).Select(a => a.Email).SingleAsync(ct);
        try
        {
            var (subject, html) = EmailTemplates.ApplicationRejected(arabic);
            await mailer.SendAsync(email, subject, html, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Rejection e-mail for application {Id} failed", id);
        }
    }

    public async Task DeleteApplicationAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var app = await db.PublisherApplications.Include(a => a.Categories).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound("application");
        db.PublisherApplications.Remove(app);
        audit.Record(admin, "application.delete", nameof(PublisherApplication), id);
        await db.SaveChangesAsync(ct);
    }

    // ---------- shared ----------

    private async Task<User> NewAccountAsync(AccountFieldsInput input, UserRole role, CancellationToken ct)
    {
        AuthService.ValidatePassword(input.Password);
        var u = new User { FullName = "", Email = "", Role = role, CreatedAtUtc = Now };
        await ApplyAccountFieldsAsync(u, input, ct);
        return u;
    }

    private async Task ApplyAccountFieldsAsync(User u, AccountFieldsInput input, CancellationToken ct)
    {
        var name = Normalize.Text(input.FullName);
        if (name is null || name.Length > 200) throw new AppException("profile.name_required");
        var email = Normalize.Email(input.Email ?? "");
        if (!email.Contains('@') || email.Length > 256) throw new AppException("auth.email_invalid");
        if (email != u.Email && await db.Users.AnyAsync(x => x.Email == email && x.Id != u.Id, ct)) throw AppException.Conflict("auth.email_taken");
        if (!await db.Countries.AnyAsync(c => c.Id == input.CountryId, ct)) throw new AppException("country.invalid");
        if (!string.IsNullOrEmpty(input.Password))
        {
            AuthService.ValidatePassword(input.Password);
            u.PasswordHash = passwords.Hash(input.Password);
            u.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        if (u.IsActive && !input.IsActive) u.SecurityStamp = Guid.NewGuid().ToString("N");
        u.FullName = name;
        u.Email = email;
        u.Phone = Normalize.Text(input.Phone);
        u.CountryId = input.CountryId;
        u.IsActive = input.IsActive;
        u.CompanyName = Normalize.Text(input.CompanyName);
        u.CrNumber = Normalize.Text(input.CrNumber);
        u.VatNumber = Normalize.Text(input.VatNumber);
        u.BankName = Normalize.Text(input.BankName);
        u.AccountNumber = Normalize.Text(input.AccountNumber);
        u.CardNumber = Normalize.Text(input.CardNumber);
    }

    private async Task ApplyKycFilesAsync(User u, UploadInput? avatar, UploadInput? cr, UploadInput? vat, UploadInput? idDoc, CancellationToken ct)
    {
        u.AvatarFileId = await files.SaveOptionalAsync(avatar, FileCategory.UserAvatar, u.Id, u.AvatarFileId, ct);
        u.CrDocumentFileId = await files.SaveOptionalAsync(cr, FileCategory.KycDocument, u.Id, u.CrDocumentFileId, ct);
        u.VatCertificateFileId = await files.SaveOptionalAsync(vat, FileCategory.KycDocument, u.Id, u.VatCertificateFileId, ct);
        u.IdDocumentFileId = await files.SaveOptionalAsync(idDoc, FileCategory.KycDocument, u.Id, u.IdDocumentFileId, ct);
    }
}

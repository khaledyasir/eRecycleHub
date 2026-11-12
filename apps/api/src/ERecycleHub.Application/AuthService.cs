using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

public sealed record SessionUser(int Id, string FullName, string Email, UserRole Role, string SecurityStamp, Guid? AvatarFileId, int? PublisherId);

public sealed record RegisterInput(string FullName, string Email, string Password, string? Phone,
    string? CompanyName, string? CrNumber, string? VatNumber, string? BankName, string? AccountNumber, string? CardNumber);

public sealed record RegisterFiles(UploadInput? Avatar, UploadInput? CrDocument, UploadInput? VatCertificate, UploadInput? IdDocument);

public sealed class AuthService(AppDbContext db, PasswordService passwords, FileService files, IEmailSender mailer, TimeProvider clock, Audit audit, ILogger<AuthService> log)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    // Verified against when the e-mail is unknown so both paths cost the same.
    private static readonly string DummyHash = new PasswordService().Hash(Guid.NewGuid().ToString());

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
            throw new AppException("auth.password_policy", 400, new { min = MinPasswordLength });
        if (password.Any(char.IsWhiteSpace)) throw new AppException("auth.password_spaces");
    }

    public async Task<SessionUser> RegisterCustomerAsync(RegisterInput input, RegisterFiles uploads, CancellationToken ct = default)
    {
        ValidatePassword(input.Password);
        var emailNorm = Normalize.Email(input.Email);
        if (await db.Users.AnyAsync(u => u.Email == emailNorm, ct)) throw AppException.Conflict("auth.email_taken");
        var countryId = await db.JordanIdAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new User
        {
            FullName = input.FullName.Trim(),
            Email = emailNorm,
            Phone = JordanPhone.Require(input.Phone),
            CountryId = countryId,
            PasswordHash = passwords.Hash(input.Password),
            Role = UserRole.Customer,
            CompanyName = Normalize.Text(input.CompanyName),
            CrNumber = Normalize.Text(input.CrNumber),
            VatNumber = Normalize.Text(input.VatNumber),
            BankName = Normalize.Text(input.BankName),
            AccountNumber = Normalize.Text(input.AccountNumber),
            CardNumber = Normalize.Text(input.CardNumber),
            CreatedAtUtc = Now,
            LastLoginAtUtc = Now,
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw AppException.Conflict("auth.email_taken");
        }
        user.AvatarFileId = await files.SaveOptionalAsync(uploads.Avatar, FileCategory.UserAvatar, user.Id, null, ct);
        user.CrDocumentFileId = await files.SaveOptionalAsync(uploads.CrDocument, FileCategory.KycDocument, user.Id, null, ct);
        user.VatCertificateFileId = await files.SaveOptionalAsync(uploads.VatCertificate, FileCategory.KycDocument, user.Id, null, ct);
        user.IdDocumentFileId = await files.SaveOptionalAsync(uploads.IdDocument, FileCategory.KycDocument, user.Id, null, ct);
        audit.Record(new Actor(user.Id, user.Role), "user.register", nameof(User), user.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToSession(user, null);
    }

    /// <summary>Generic failure for unknown e-mail, wrong password, inactive account or wrong role (no account enumeration).</summary>
    public async Task<SessionUser> LoginAsync(string email, string password, UserRole? requiredRole, string? ip, CancellationToken ct = default)
    {
        var emailNorm = Normalize.Email(email ?? "");
        var user = await db.Users.Include(u => u.Publisher).FirstOrDefaultAsync(u => u.Email == emailNorm, ct);
        if (user is null)
        {
            passwords.Verify(DummyHash, password ?? "", out _);
            throw new AppException("auth.invalid_credentials", 401);
        }

        var now = Now;
        if (user.LockoutEndUtc > now) throw new AppException("auth.locked", 429, new { minutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes) });

        var ok = passwords.Verify(user.PasswordHash, password ?? "", out var rehash);
        if (!ok)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = now + LockoutDuration;
                user.FailedLoginCount = 0;
                log.LogWarning("Account {UserId} locked after repeated failed logins from {Ip}", user.Id, ip);
            }
            await db.SaveChangesAsync(ct);
            throw new AppException("auth.invalid_credentials", 401);
        }

        if (!user.IsActive || user.IsDeleted || (requiredRole is not null && user.Role != requiredRole))
            throw new AppException("auth.invalid_credentials", 401);

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;
        if (rehash) user.PasswordHash = passwords.Hash(password!);
        await db.SaveChangesAsync(ct);
        return ToSession(user, user.Publisher?.Id);
    }

    /// <summary>Used on every authenticated request: the session is valid only while the account is active and its stamp unchanged.</summary>
    public Task<SessionUser?> GetSessionAsync(int userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive && !u.IsDeleted)
            .Select(u => new SessionUser(u.Id, u.FullName, u.Email, u.Role, u.SecurityStamp, u.AvatarFileId, u.Publisher == null ? null : u.Publisher.Id))
            .FirstOrDefaultAsync(ct);

    /// <summary>Changes the password and the security stamp (ending every other session). Returns the refreshed session.</summary>
    public async Task<SessionUser> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        var user = await db.Users.Include(u => u.Publisher).SingleAsync(u => u.Id == userId, ct);
        if (!passwords.Verify(user.PasswordHash, currentPassword ?? "", out _)) throw new AppException("auth.current_password_wrong");
        user.PasswordHash = passwords.Hash(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record(new Actor(userId, user.Role), "user.password_change", nameof(User), userId);
        await db.SaveChangesAsync(ct);
        return ToSession(user, user.Publisher?.Id);
    }

    /// <summary>Always completes silently: the caller cannot tell whether the address has an account.</summary>
    public async Task RequestPasswordResetAsync(string email, string resetUrlBase, bool arabic, CancellationToken ct = default)
    {
        var emailNorm = Normalize.Email(email ?? "");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNorm && u.IsActive && !u.IsDeleted, ct);
        if (user is null) return;

        var token = await NewResetTokenAsync(user.Id, ct);
        var (subject, html) = EmailTemplates.PasswordReset(arabic, resetUrlBase + Uri.EscapeDataString(token));
        try
        {
            await mailer.SendAsync(user.Email, subject, html, ct);
        }
        catch (Exception ex)
        {
            // Delivery failure must not change the response the requester sees.
            log.LogError(ex, "Password reset email for user {UserId} could not be sent", user.Id);
        }
    }

    /// <summary>
    /// Phone reset, after the caller has checked the SMS code: a reset token (same as the e-mailed one, used with ResetPasswordAsync) for the
    /// single active account with this number. An unknown or shared number gets the same error as a wrong code.
    /// </summary>
    public async Task<string> PhoneResetTokenAsync(string phone, CancellationToken ct = default) =>
        await ResetTokenForAsync(phone, ct) ?? throw new AppException("otp.invalid");

    /// <summary>
    /// After a verified one-time code: a reset token for the single active account with this phone number (07XXXXXXXX) or e-mail address,
    /// or null when there is none (or several share the number).
    /// </summary>
    public async Task<string?> ResetTokenForAsync(string target, CancellationToken ct = default)
    {
        var t = Normalize.Text(target);
        if (t is null) return null;
        var q = db.Users.Where(u => u.IsActive && !u.IsDeleted);
        q = t.Contains('@') ? q.Where(u => u.Email == Normalize.Email(t)) : q.Where(u => u.Phone == t);
        var ids = await q.Select(u => u.Id).Take(2).ToListAsync(ct);
        return ids.Count == 1 ? await NewResetTokenAsync(ids[0], ct) : null;
    }

    private async Task<string> NewResetTokenAsync(int userId, CancellationToken ct)
    {
        var now = Now;
        // A new request supersedes older unused tokens.
        await db.PasswordResetTokens.Where(t => t.UserId == userId && t.UsedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), ct);
        var token = PasswordService.NewToken();
        db.PasswordResetTokens.Add(new PasswordResetToken { UserId = userId, TokenHash = PasswordService.Sha256(token), CreatedAtUtc = now, ExpiresAtUtc = now + ResetTokenLifetime });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> IsResetTokenValidAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var hash = PasswordService.Sha256(token);
        var now = Now;
        return await db.PasswordResetTokens.AnyAsync(t => t.TokenHash == hash && t.UsedAtUtc == null && t.ExpiresAtUtc > now, ct);
    }

    /// <summary>Returns the id of the account whose password was reset.</summary>
    public async Task<int> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        var hash = PasswordService.Sha256(token ?? "");
        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Claiming the token with a conditional UPDATE makes it single-use even under concurrent submissions.
        var claimed = await db.PasswordResetTokens.Where(t => t.TokenHash == hash && t.UsedAtUtc == null && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), ct);
        if (claimed == 0) throw new AppException("auth.reset_token_invalid");
        var userId = await db.PasswordResetTokens.Where(t => t.TokenHash == hash).Select(t => t.UserId).SingleAsync(ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (!user.IsActive || user.IsDeleted) throw new AppException("auth.reset_token_invalid");
        user.PasswordHash = passwords.Hash(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        audit.Record(new Actor(userId, user.Role), "user.password_reset", nameof(User), userId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return userId;
    }

    private static SessionUser ToSession(User u, int? publisherId) => new(u.Id, u.FullName, u.Email, u.Role, u.SecurityStamp, u.AvatarFileId, publisherId);
}

using System.Security.Cryptography;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Application;

/// <summary>
/// Jordanian mobile numbers as the mobile app sends them (E.164, "+9627XXXXXXXX") and as the database stores them ("07XXXXXXXX").
/// </summary>
public static class MobilePhone
{
    /// <summary>Accepts +9627…, 009627…, 9627… or 07… (spaces and dashes ignored). Returns 07XXXXXXXX, or null when it is not a Jordanian mobile number.</summary>
    public static string? ToLocal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var plus = input.TrimStart().StartsWith('+');
        var digits = new string(input.Where(c => c is not (' ' or '-' or '(' or ')' or '+')).ToArray());
        if (!digits.All(char.IsAsciiDigit)) return null;
        if (digits.StartsWith("00962")) digits = digits[5..];
        else if (digits.StartsWith("962") && (plus || digits.Length == 12)) digits = digits[3..];
        if (digits.Length == 9 && digits[0] == '7') digits = "0" + digits;
        return JordanPhone.IsValid(digits) ? digits : null;
    }

    /// <summary>07XXXXXXXX → +9627XXXXXXXX. Anything else (legacy data) is returned unchanged.</summary>
    public static string? ToE164(string? stored) => stored is not null && JordanPhone.IsValid(stored) ? "+962" + stored[1..] : stored;
}

public sealed class TokenSessionOptions
{
    /// <summary>Bearer token lifetime. The app has no refresh flow, so this is long.</summary>
    public int TokenDays { get; set; } = 30;
}

public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc, Guid SessionId);

public sealed record ActiveSession(Guid SessionId, SessionUser User);

/// <summary>Opaque bearer tokens backed by <see cref="UserSession"/> rows (only the SHA-256 of a token is stored).</summary>
public sealed class SessionService(AppDbContext db, TimeProvider clock, IOptions<TokenSessionOptions> options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IssuedToken> IssueAsync(int userId, CancellationToken ct = default)
    {
        var token = PasswordService.NewToken();
        var now = Now;
        var session = new UserSession
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = PasswordService.Sha256(token), CreatedAtUtc = now, LastSeenAtUtc = now,
            ExpiresAtUtc = now.AddDays(Math.Clamp(options.Value.TokenDays, 1, 365)),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new IssuedToken(token, session.ExpiresAtUtc, session.Id);
    }

    /// <summary>The session for a bearer token, or null when it is unknown, expired, revoked, or its account is inactive or deleted.</summary>
    public async Task<ActiveSession?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        var hash = PasswordService.Sha256(token);
        var now = Now;
        var row = await db.UserSessions.AsNoTracking()
            .Where(s => s.TokenHash == hash && s.RevokedAtUtc == null && s.ExpiresAtUtc > now && s.User!.IsActive && !s.User.IsDeleted)
            .Select(s => new
            {
                s.Id, s.LastSeenAtUtc,
                User = new SessionUser(s.User!.Id, s.User.FullName, s.User.Email, s.User.Role, s.User.SecurityStamp, s.User.AvatarFileId,
                    s.User.Publisher == null ? null : s.User.Publisher.Id),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        if (row.LastSeenAtUtc is null || now - row.LastSeenAtUtc > TimeSpan.FromMinutes(15))
            await db.UserSessions.Where(s => s.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAtUtc, now), ct);
        return new ActiveSession(row.Id, row.User);
    }

    /// <summary>Ends one session and forgets the push token(s) registered by it.</summary>
    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var now = Now;
        await db.DeviceTokens.Where(d => d.SessionId == sessionId).ExecuteDeleteAsync(ct);
        await db.UserSessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
    }

    /// <summary>Ends every session of the user except <paramref name="keep"/>, and their push tokens.</summary>
    public async Task RevokeAllAsync(int userId, Guid? keep = null, CancellationToken ct = default)
    {
        var now = Now;
        await db.DeviceTokens.Where(d => d.UserId == userId && (keep == null || d.SessionId != keep)).ExecuteDeleteAsync(ct);
        await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null && (keep == null || s.Id != keep))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
    }

    /// <summary>Upserts a push token for the user and session (a token re-sent on every sign-in is fine; it moves to the latest user).</summary>
    public async Task RegisterDeviceAsync(int userId, Guid sessionId, string? token, CancellationToken ct = default)
    {
        var t = Normalize.Text(token);
        if (t is null || t.Length > 450) throw new AppException("device.token_invalid", 422);
        var now = Now;
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == t, ct);
        if (existing is null) db.DeviceTokens.Add(new DeviceToken { UserId = userId, SessionId = sessionId, Token = t, CreatedAtUtc = now, UpdatedAtUtc = now });
        else (existing.UserId, existing.SessionId, existing.UpdatedAtUtc) = (userId, sessionId, now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* a concurrent registration of the same token won; it points at this user too */ }
    }
}

public sealed class OtpOptions
{
    /// <summary>Development/testing only (the host clears it in Production): every code is this value instead of a random one.</summary>
    public string? FixedCode { get; set; }
    public int Length { get; set; } = 5;
    public int LifetimeMinutes { get; set; } = 10;
    public int MaxAttempts { get; set; } = 5;
    public int MaxSendsPerHour { get; set; } = 5;
    /// <summary>How long a verified code allows registration with that phone number.</summary>
    public int RegistrationWindowMinutes { get; set; } = 30;
}

/// <summary>Delivers a one-time code. E-mail goes through <see cref="IEmailSender"/>; SMS needs a provider (see <see cref="LoggingSmsSender"/>).</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneE164, string message, CancellationToken ct = default);
}

/// <summary>
/// Placeholder until an SMS provider is chosen: writes the message to the log. The host only registers it outside Production;
/// in Production phone codes fail with otp.unavailable until a real <see cref="ISmsSender"/> is registered.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> log) : ISmsSender
{
    public Task SendAsync(string phoneE164, string message, CancellationToken ct = default)
    {
        log.LogWarning("SMS (not sent, no provider) to {Phone}: {Message}", phoneE164, message);
        return Task.CompletedTask;
    }
}

public sealed class OtpService(AppDbContext db, TimeProvider clock, IOptions<OtpOptions> options, IEmailSender mailer, IServiceProvider services, ILogger<OtpService> log)
{
    private OtpOptions O => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The app sends a phone number, or (forgot password by e-mail) an e-mail address, in the same "phone" field.</summary>
    public static string NormalizeTarget(string? raw)
    {
        var t = Normalize.Text(raw) ?? throw new AppException("phone.invalid", 422);
        if (t.Contains('@'))
        {
            var email = Normalize.Email(t);
            return email.Length <= 256 && email.IndexOf('@') > 0 && email.IndexOf('.', email.IndexOf('@')) > 0 ? email : throw new AppException("phone.invalid", 422);
        }
        return MobilePhone.ToLocal(t) ?? throw new AppException("phone.invalid", 422);
    }

    private static string Hash(string target, string code) => PasswordService.Sha256(target + ":" + code);

    public async Task SendAsync(string? raw, bool arabic, CancellationToken ct = default)
    {
        var target = NormalizeTarget(raw);
        var now = Now;
        var since = now.AddHours(-1);
        if (await db.OtpCodes.CountAsync(c => c.Target == target && c.CreatedAtUtc > since, ct) >= O.MaxSendsPerHour)
            throw new AppException("otp.too_many", 429, new { retryAfterSeconds = 3600 });

        var isEmail = target.Contains('@');
        var sms = isEmail ? null : services.GetService(typeof(ISmsSender)) as ISmsSender;
        if (!isEmail && sms is null) throw new AppException("otp.unavailable", 503);

        var code = string.IsNullOrEmpty(O.FixedCode) ? RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, O.Length)).ToString("D" + O.Length) : O.FixedCode;
        // A new code replaces any earlier unverified one.
        await db.OtpCodes.Where(c => c.Target == target && c.VerifiedAtUtc == null && c.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAtUtc, now), ct);
        db.OtpCodes.Add(new OtpCode { Target = target, CodeHash = Hash(target, code), CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(O.LifetimeMinutes) });
        await db.SaveChangesAsync(ct);

        try
        {
            if (isEmail)
            {
                var (subject, html) = EmailTemplates.OneTimeCode(arabic, code);
                await mailer.SendAsync(target, subject, html, ct);
            }
            else
            {
                var text = arabic ? $"رمز التحقق في eRecycleHub: {code}" : $"Your eRecycleHub verification code: {code}";
                await sms!.SendAsync(MobilePhone.ToE164(target)!, text, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "One-time code could not be delivered");
            throw new AppException("otp.unavailable", 503);
        }
    }

    /// <summary>Checks the latest code for the target. Returns the normalised target. Too many wrong tries burn the code.</summary>
    public async Task<string> VerifyAsync(string? raw, string? code, CancellationToken ct = default)
    {
        var target = NormalizeTarget(raw);
        var now = Now;
        var row = await db.OtpCodes.Where(c => c.Target == target && c.VerifiedAtUtc == null && c.ExpiresAtUtc > now)
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct) ?? throw new AppException("otp.invalid", 422);
        var given = Normalize.Text(code) ?? "";
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Hash(target, given)), System.Text.Encoding.ASCII.GetBytes(row.CodeHash)))
        {
            row.FailedAttempts++;
            if (row.FailedAttempts >= O.MaxAttempts) row.ExpiresAtUtc = now;
            await db.SaveChangesAsync(ct);
            throw new AppException("otp.invalid", 422);
        }
        // Conditional update: a code verifies once even under concurrent requests.
        var claimed = await db.OtpCodes.Where(c => c.Id == row.Id && c.VerifiedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VerifiedAtUtc, now), ct);
        if (claimed == 0) throw new AppException("otp.invalid", 422);
        return target;
    }

    /// <summary>Registration proof: a code for this phone verified recently and not used yet. Consumes it.</summary>
    public async Task ConsumeVerifiedAsync(string target, CancellationToken ct = default)
    {
        var since = Now.AddMinutes(-O.RegistrationWindowMinutes);
        var id = await db.OtpCodes.Where(c => c.Target == target && c.VerifiedAtUtc >= since && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.Id).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct);
        var now = Now;
        if (id is null || await db.OtpCodes.Where(c => c.Id == id && c.ConsumedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now), ct) == 0)
            throw new AppException("otp.not_verified", 422);
    }
}

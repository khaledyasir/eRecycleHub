using System.Text.RegularExpressions;
using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api.Mobile;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record OtpSendRequest(string? Phone);
public sealed record OtpVerifyRequest(string? Phone, string? Code);
public sealed record RegisterRequest(string? Type, int CountryId, string? FullName, string? Phone, string? Email, string? Password);
public sealed record PasswordResetRequest(string? Phone, string? Password, string? ResetToken);

/// <summary>§3.1 Auth. Everything here is public except logout.</summary>
public static partial class AuthEndpoints
{
    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailPattern();

    public static void MapAuthEndpoints(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/auth").WithTags("Auth");

        g.MapPost("/login", async (LoginRequest req, HttpContext ctx, AuthService auth, SessionService sessions, CancellationToken ct) =>
        {
            // Only customer accounts use the app; an administrator gets the same answer as a wrong password.
            var user = await auth.LoginAsync(req.Email ?? "", req.Password ?? "", UserRole.Customer, ctx.Connection.RemoteIpAddress?.ToString(), ct);
            var token = await sessions.IssueAsync(user.Id, ct);
            return Reply.Data(new AuthTokenDto(token.Token, token.ExpiresAtUtc));
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth).Produces<Envelope<AuthTokenDto>>()
            .WithSummary("Sign in with e-mail and password").WithDescription("401 INVALID_CREDENTIALS for a wrong e-mail or password; 429 RATE_LIMITED after 5 failures (15-minute lock).");

        g.MapPost("/otp/send", async (OtpSendRequest req, HttpContext ctx, OtpService otp, CancellationToken ct) =>
        {
            await otp.SendAsync(req.Phone, ctx.Request.IsArabic(), ct);
            return Reply.Done();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Forms).Produces<Envelope<object>>()
            .WithSummary("Send a 5-digit code").WithDescription("`phone` is a Jordanian mobile number (+9627XXXXXXXX) or, from Forgot password, an e-mail address. Answers the same for known and unknown targets. 5 codes per target per hour.");

        g.MapPost("/otp/verify", async (OtpVerifyRequest req, OtpService otp, AuthService auth, CancellationToken ct) =>
        {
            var target = await otp.VerifyAsync(req.Phone, req.Code, ct);
            return Reply.Data(new OtpVerifyDto(await auth.ResetTokenForAsync(target, ct)));
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth).Produces<Envelope<OtpVerifyDto>>()
            .WithSummary("Check the code").WithDescription("422 INVALID_OTP when wrong or expired (5 wrong tries burn the code). `resetToken` is set when an account exists for the phone/e-mail; send it to /auth/password/reset.");

        g.MapPost("/register", async (RegisterRequest req, AuthService auth, OtpService otp, SessionService sessions, AppDbContext db, CancellationToken ct) =>
        {
            var name = Normalize.Text(req.FullName);
            if (name is null || name.Length > 200) throw MobileAccount.Invalid("fullName", "Name is required (200 characters at most).");
            var email = Normalize.Text(req.Email);
            if (email is null || email.Length > 256 || !EmailPattern().IsMatch(email)) throw MobileAccount.Invalid("email", "Enter a valid e-mail address.");
            email = Normalize.Email(email);
            var phone = MobilePhone.ToLocal(req.Phone) ?? throw MobileAccount.Invalid("phone", "Enter a valid Jordanian mobile number.");
            AuthService.ValidatePassword(req.Password);
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw MobileAccount.Invalid("email", "The email has already been taken.");
            if (await db.Users.AnyAsync(u => u.Phone == phone && !u.IsDeleted, ct)) throw MobileAccount.Invalid("phone", "The phone has already been taken.");

            // The app always sends countryId 1 (docs M3) and the platform serves Jordan only: an unknown id falls back to the allowed country.
            var countryId = await db.Countries.Allowed().Where(c => c.IsActive && c.Id == req.CountryId).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
                ?? await db.Countries.Allowed().Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
                ?? throw new AppException("country.invalid");

            // Proof that this phone passed /auth/otp/verify in the last 30 minutes (single use).
            await otp.ConsumeVerifiedAsync(phone, ct);
            var user = await auth.RegisterCustomerAsync(new RegisterInput(name, email, req.Password!, phone, countryId, null, null, null, null, null, null),
                new RegisterFiles(null, null, null, null), ct);
            var token = await sessions.IssueAsync(user.Id, ct);
            return Reply.Data(new AuthTokenDto(token.Token, token.ExpiresAtUtc));
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Forms).Produces<Envelope<AuthTokenDto>>()
            .WithSummary("Create a customer account (after /auth/otp/verify)")
            .WithDescription("`type` is always Customer. Requires a code for `phone` verified in the last 30 minutes (422 OTP_NOT_VERIFIED otherwise). Duplicate e-mail/phone: 422 VALIDATION_FAILED with `errors`.");

        g.MapPost("/password/reset", async (PasswordResetRequest req, AuthService auth, SessionService sessions, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(req.ResetToken)) throw new AppException("auth.reset_token_invalid", 422);
            var userId = await auth.ResetPasswordAsync(req.ResetToken, req.Password ?? "", ct);
            await sessions.RevokeAllAsync(userId, null, ct);
            return Reply.Done();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth).Produces<Envelope<object>>()
            .WithSummary("Set a new password with the resetToken from /auth/otp/verify").WithDescription("Signs the account out everywhere. 422 INVALID_RESET_TOKEN when missing, used or expired (1 hour).");

        g.MapPost("/logout", async (HttpContext ctx, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.RevokeAsync(ctx.SessionId(), ct);
            return Reply.Done();
        }).Produces<Envelope<object>>().WithSummary("End this session").WithDescription("Also forgets the push token registered by this session.");
    }
}

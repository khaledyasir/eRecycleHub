using ERecycleHub.Application;
using ERecycleHub.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Api.Endpoints;

public sealed record MeResponse(SessionUserDto? User, string CsrfToken);
public sealed record SessionUserDto(int Id, string FullName, string Email, UserRole Role, Guid? AvatarFileId, int? PublisherId);
public sealed record LoginRequest(string Email, string Password, bool AdminOnly = false);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record TokenValidResponse(bool Valid);
public sealed record RefreshRequest(string RefreshToken);
public sealed record OtpSendRequest(string Phone);
public sealed record OtpVerifyRequest(string Phone, string Code);
public sealed record OtpVerifyResponse(string ResetToken);

public static class AuthEndpoints
{
    private static SessionUserDto Dto(SessionUser u) => new(u.Id, u.FullName, u.Email, u.Role, u.AvatarFileId, u.PublisherId);

    /// <summary>Writes <see cref="AccessTokenResponse"/> {tokenType, accessToken, expiresIn, refreshToken} for a token client.</summary>
    private static IResult TokenSignIn(ClaimsPrincipal principal) => Results.SignIn(principal, authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
    private static IResult TokenSignIn(SessionUser user) => TokenSignIn(Claims.Principal(user, BearerTokenDefaults.AuthenticationScheme));

    // ponytail: no SMS provider yet, so outside Production every number "receives" the fixed code in Otp:FixedCode. Production refuses it:
    // a known code would let anyone reset any account's password from its phone number. Add an SMS sender and stored random codes first.
    private static string FixedOtp(HttpContext ctx)
    {
        var code = ctx.RequestServices.GetRequiredService<IConfiguration>()["Otp:FixedCode"];
        return ctx.RequestServices.GetRequiredService<IHostEnvironment>().IsProduction() || string.IsNullOrEmpty(code)
            ? throw new AppException("otp.unavailable", 503)
            : code;
    }

    private static async Task<MeResponse> SignInAsync(HttpContext ctx, SessionUser user)
    {
        var principal = Claims.Principal(user, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return new MeResponse(Dto(user), Security.IssueCsrfToken(ctx, principal));
    }

    public static string PublicUrl(this HttpContext ctx) =>
        (ctx.RequestServices.GetRequiredService<IConfiguration>()["App:PublicUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Auth");

        g.MapGet("/me", async (HttpContext ctx, AuthService auth, CancellationToken ct) =>
        {
            var actor = ctx.ActorOrNull();
            var user = actor is null ? null : await auth.GetSessionAsync(actor.UserId, ct);
            return new MeResponse(user is null ? null : Dto(user), Security.IssueCsrfToken(ctx));
        }).AllowAnonymous();

        g.MapPost("/login", async (LoginRequest req, HttpContext ctx, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.LoginAsync(req.Email, req.Password, req.AdminOnly ? UserRole.Admin : null, ctx.Connection.RemoteIpAddress?.ToString(), ct);
            return await SignInAsync(ctx, user);
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth);

        // ---- mobile app (token client: send X-Client: mobile until it has a token, then Authorization: Bearer) ----
        g.MapPost("/token", async (LoginRequest req, HttpContext ctx, AuthService auth, CancellationToken ct) =>
            TokenSignIn(await auth.LoginAsync(req.Email, req.Password, req.AdminOnly ? UserRole.Admin : null, ctx.Connection.RemoteIpAddress?.ToString(), ct)))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Auth).Produces<AccessTokenResponse>()
            .WithSummary("Sign in for the mobile app").WithDescription("Returns a Bearer access token (expiresIn seconds, 1 hour) and a refresh token (30 days). Then call GET /api/auth/me for the user. A password change or reset ends all tokens.");

        g.MapPost("/token/refresh", async (RefreshRequest req, HttpContext ctx, IOptionsMonitor<BearerTokenOptions> options, TimeProvider clock) =>
        {
            var ticket = options.Get(BearerTokenDefaults.AuthenticationScheme).RefreshTokenProtector.Unprotect(req.RefreshToken);
            var principal = ticket?.Properties.ExpiresUtc > clock.GetUtcNow() ? await Security.SessionPrincipalAsync(ctx, ticket.Principal) : null;
            return principal is null ? throw new AppException("auth.refresh_invalid", 401) : TokenSignIn(principal);
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth).Produces<AccessTokenResponse>()
            .WithSummary("New tokens from a refresh token").WithDescription("401 auth.refresh_invalid when the refresh token is expired, malformed, or the account's session has ended: sign in again.");

        g.MapPost("/otp/send", (OtpSendRequest req, HttpContext ctx) =>
        {
            FixedOtp(ctx);
            if (!JordanPhone.IsValid(Normalize.Text(req.Phone))) throw new AppException("phone.invalid");
            return Results.NoContent(); // identical for known and unknown numbers
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Forms)
            .WithSummary("Password reset by phone, step 1: send the code").WithDescription("No SMS provider yet: outside Production the code is always the configured fixed code; in Production this returns 503 otp.unavailable.");

        g.MapPost("/otp/verify", async (OtpVerifyRequest req, HttpContext ctx, AuthService auth, CancellationToken ct) =>
            req.Code == FixedOtp(ctx) ? new OtpVerifyResponse(await auth.PhoneResetTokenAsync(req.Phone, ct)) : throw new AppException("otp.invalid"))
            .AllowAnonymous().RequireRateLimiting(RateLimits.Auth)
            .WithSummary("Password reset by phone, step 2: check the code").WithDescription("Returns a resetToken (valid 1 hour). Step 3: POST /api/auth/reset-password {token: resetToken, password}. Wrong code, unknown number or a number shared by several accounts: 400 otp.invalid.");

        g.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var anonymous = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
            return new MeResponse(null, Security.IssueCsrfToken(ctx, anonymous));
        }).AllowAnonymous();

        g.MapPost("/register", async (HttpContext ctx, AuthService auth, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<RegisterInput>(ct);
            var user = await auth.RegisterCustomerAsync(data, new RegisterFiles(files.Upload("avatar"), files.Upload("crDocument"), files.Upload("vatCertificate"), files.Upload("idDocument")), ct);
            return await SignInAsync(ctx, user);
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Forms).Accepts<RegisterInput>("multipart/form-data").Produces<MeResponse>();

        g.MapPost("/forgot-password", async (ForgotPasswordRequest req, HttpContext ctx, AuthService auth, CancellationToken ct) =>
        {
            var locale = ctx.Request.IsArabic() ? "ar" : "en";
            await auth.RequestPasswordResetAsync(req.Email, $"{ctx.PublicUrl()}/{locale}/reset-password?token=", locale == "ar", ct);
            return Results.NoContent(); // identical for known and unknown addresses
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Forms);

        g.MapGet("/reset-password/validate", async (string token, AuthService auth, CancellationToken ct) =>
            new TokenValidResponse(await auth.IsResetTokenValidAsync(token, ct))).AllowAnonymous().RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/reset-password", async (ResetPasswordRequest req, AuthService auth, CancellationToken ct) =>
        {
            await auth.ResetPasswordAsync(req.Token, req.Password, ct);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Auth);

        g.MapPost("/change-password", async (ChangePasswordRequest req, HttpContext ctx, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.ChangePasswordAsync(ctx.Actor().UserId, req.CurrentPassword, req.NewPassword, ct);
            // Re-issue this session with the new stamp (all others are now invalid): new tokens for a token client, a new cookie otherwise.
            return Security.HasBearer(ctx.Request) ? TokenSignIn(user) : Results.Ok(await SignInAsync(ctx, user));
        }).RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimits.Auth).Produces<MeResponse>()
            .WithDescription("Cookie session: returns MeResponse. Bearer client: returns new tokens (AccessTokenResponse); the old ones stop working.");
    }
}

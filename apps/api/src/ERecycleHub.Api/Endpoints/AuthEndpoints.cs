using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ERecycleHub.Api.Endpoints;

public sealed record MeResponse(SessionUserDto? User, string CsrfToken);
public sealed record SessionUserDto(int Id, string FullName, string Email, UserRole Role, Guid? AvatarFileId, int? PublisherId);
public sealed record LoginRequest(string Email, string Password, bool AdminOnly = false);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record TokenValidResponse(bool Valid);

public static class AuthEndpoints
{
    private static SessionUserDto Dto(SessionUser u) => new(u.Id, u.FullName, u.Email, u.Role, u.AvatarFileId, u.PublisherId);

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
            return await SignInAsync(ctx, user); // re-issue this session's cookie with the new stamp; all others are now invalid
        }).RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimits.Auth);
    }
}

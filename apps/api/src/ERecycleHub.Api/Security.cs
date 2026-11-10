using System.Security.Claims;
using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ERecycleHub.Api;

public static class Policies
{
    public const string Member = "member";
    public const string Admin = "admin";
    public const string Publisher = "publisher";
}

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Forms = "forms";
    public const string Bids = "bids";
}

public static class Security
{
    public const string CsrfHeader = "X-CSRF-TOKEN";

    /// <summary>
    /// Runs on every authenticated request: the cookie is only honoured while the account is active and its security stamp,
    /// role and publisher link are unchanged. Password change/reset, deactivation and deletion therefore end sessions at once.
    /// </summary>
    public static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (principal is null || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            ctx.RejectPrincipal();
            return;
        }
        var auth = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
        var session = await auth.GetSessionAsync(userId, ctx.HttpContext.RequestAborted);
        if (session is null
            || session.SecurityStamp != principal.FindFirstValue(Claims.Stamp)
            || session.Role.ToString() != principal.FindFirstValue(ClaimTypes.Role))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        // Becoming (or ceasing to be) a seller changes the profile link but not the identity: re-issue the cookie from the database state
        // instead of signing the user out. The claim always comes from the database, never from the client.
        if (session.PublisherId?.ToString() != principal.FindFirstValue(Claims.PublisherId))
        {
            ctx.ReplacePrincipal(Claims.Principal(session, CookieAuthenticationDefaults.AuthenticationScheme));
            ctx.ShouldRenew = true;
        }
    }

    public const string AutoScheme = "auto";
    public const string ClientHeader = "X-Client";

    /// <summary>The request carries an Authorization: Bearer token (mobile app, Swagger).</summary>
    public static bool HasBearer(HttpRequest req) => req.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Token client (mobile app): sends a Bearer token or <c>X-Client: mobile</c> (for calls made before it has a token). Such requests
    /// need no CSRF token: a browser cannot add a custom header to a cross-site request without a CORS preflight, and this API allows none.
    /// </summary>
    public static bool IsTokenClient(HttpRequest req) => HasBearer(req) || req.Headers[ClientHeader].ToString() == "mobile";

    /// <summary>
    /// The Bearer counterpart of <see cref="ValidateSessionAsync"/>: a token is only honoured while the account is active and its security stamp
    /// and role are unchanged, so password change/reset, deactivation and deletion end mobile sessions at once. The principal is rebuilt from
    /// the database, so the seller claim is always current.
    /// </summary>
    public static async Task ValidateBearerAsync(HttpContext ctx, RequestDelegate next)
    {
        var principal = ctx.User;
        if (principal.Identity is { IsAuthenticated: true, AuthenticationType: BearerTokenDefaults.AuthenticationScheme })
            ctx.User = await SessionPrincipalAsync(ctx, principal) ?? new ClaimsPrincipal(new ClaimsIdentity());
        await next(ctx);
    }

    /// <summary>A fresh Bearer principal for the user in <paramref name="principal"/>, or null when that session has ended.</summary>
    public static async Task<ClaimsPrincipal?> SessionPrincipalAsync(HttpContext ctx, ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var session = await ctx.RequestServices.GetRequiredService<AuthService>().GetSessionAsync(userId, ctx.RequestAborted);
        return session is not null && session.SecurityStamp == principal.FindFirstValue(Claims.Stamp) && session.Role.ToString() == principal.FindFirstValue(ClaimTypes.Role)
            ? Claims.Principal(session, BearerTokenDefaults.AuthenticationScheme)
            : null;
    }

    /// <summary>Validates the antiforgery token on every state-changing /api request (header <see cref="CsrfHeader"/>), except from token clients.</summary>
    public static async Task CsrfAsync(HttpContext ctx, RequestDelegate next)
    {
        var m = ctx.Request.Method;
        var unsafeMethod = !(HttpMethods.IsGet(m) || HttpMethods.IsHead(m) || HttpMethods.IsOptions(m) || HttpMethods.IsTrace(m));
        if (unsafeMethod && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/v1") && !HasBearer(ctx.Request))
        {
            var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(ctx);
            }
            catch (AntiforgeryValidationException)
            {
                throw new AppException("csrf.invalid", 400);
            }
        }
        await next(ctx);
    }

    /// <summary>Issues a request token for the current identity (call after changing who is signed in).</summary>
    public static string IssueCsrfToken(HttpContext ctx, ClaimsPrincipal? user = null)
    {
        if (user is not null) ctx.User = user;
        return ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx).RequestToken!;
    }

    public static Task HeadersAsync(HttpContext ctx, RequestDelegate next)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            // File responses set their own (frameable by the same origin for PDF viewing); everything else is JSON and never framed.
            // The Swagger UI page is an HTML app; 'none' would leave it blank.
            if (ctx.Request.Path.StartsWithSegments("/api/swagger")) h.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
            else if (!h.ContainsKey("Content-Security-Policy"))
            {
                h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                h.XFrameOptions = "DENY";
            }
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}

public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection s) => s
        .AddScoped<Audit>()
        .AddScoped<FileService>()
        .AddScoped<AuthService>()
        .AddScoped<SessionService>()
        .AddScoped<OtpService>()
        .AddScoped<BiddingService>()
        .AddScoped<WalletService>()
        .AddScoped<AuctionQueries>()
        .AddScoped<AuctionManagement>()
        .AddScoped<PublicContent>()
        .AddScoped<AccountService>()
        .AddScoped<SellerService>()
        .AddScoped<AdminPeople>()
        .AddScoped<CmsService>()
        .AddScoped<ReportsService>()
        .AddScoped<MobileCatalog>()
        .AddScoped<MobileAccount>();
}

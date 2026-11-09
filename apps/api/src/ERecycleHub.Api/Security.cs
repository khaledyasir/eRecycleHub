using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;

namespace ERecycleHub.Api;

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Forms = "forms";
    public const string Bids = "bids";
}

public static class Security
{
    public static Task HeadersAsync(HttpContext ctx, RequestDelegate next)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // The Swagger UI page is an HTML app; 'none' would leave it blank. File responses set their own policy.
            if (ctx.Request.Path.StartsWithSegments("/api/swagger"))
                h.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
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

using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ERecycleHub.Api;
using ERecycleHub.Api.Endpoints;
using ERecycleHub.Application;
using ERecycleHub.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."), sql => sql.CommandTimeout(30)));

builder.Services.Configure<StorageOptions>(config.GetSection("Storage"));
builder.Services.Configure<EmailOptions>(config.GetSection("Email"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordService>();
if (config["Email:Provider"] == "SendGrid") builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
else builder.Services.AddScoped<IEmailSender, PickupEmailSender>();
builder.Services.AddApplicationServices();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict; // amounts must be JSON numbers, never strings
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "eRecycleHub API";
    doc.Info.Version = "v1";
    return Task.CompletedTask;
}));

const long MaxUpload = 60 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxUpload);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUpload);

// ---- authentication: one HttpOnly cookie, re-validated against the database on every request ----
var secureCookies = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "erh.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = secureCookies;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = Security.ValidateSessionAsync;
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser())
    .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(ERecycleHub.Domain.UserRole.Admin)))
    .AddPolicy(Policies.Publisher, p => p.RequireRole(nameof(ERecycleHub.Domain.UserRole.Publisher)).RequireClaim(Claims.PublisherId));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = Security.CsrfHeader;
    o.Cookie.Name = "erh.af";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies;
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (c, ct) =>
    {
        c.HttpContext.Response.ContentType = "application/problem+json";
        await c.HttpContext.Response.WriteAsync("""{"status":429,"title":"rate.limited","code":"rate.limited"}""", ct);
    };
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.AddPolicy(RateLimits.Auth, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimits.Forms, c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:FormsPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimits.Bids, c => RateLimitPartition.GetFixedWindowLimiter(c.ActorOrNull()?.UserId.ToString() ?? Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:BidsPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // The API is only reachable through the Next.js server / reverse proxy, which sets X-Forwarded-For.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (args.Length > 0 && await Commands.RunAsync(app, args)) return;

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(Security.HeadersAsync);
app.UseAuthentication();
app.UseAuthorization();
app.Use(Security.CsrfAsync);
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

var api = app.MapGroup("/api").DisableAntiforgery(); // antiforgery is enforced for every unsafe /api request by Security.CsrfAsync
api.MapAuthEndpoints();
api.MapPublicEndpoints();
api.MapAccountEndpoints();
api.MapSellerEndpoints();
api.MapAdminEndpoints();
api.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem(statusCode: 503, title: "db.unavailable")).AllowAnonymous();

app.Run();

public partial class Program;

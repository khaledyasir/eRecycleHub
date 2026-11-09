using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ERecycleHub.Api;
using ERecycleHub.Api.Mobile;
using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."), sql => sql.CommandTimeout(30)));

builder.Services.Configure<StorageOptions>(config.GetSection("Storage"));
builder.Services.Configure<EmailOptions>(config.GetSection("Email"));
builder.Services.Configure<TokenSessionOptions>(config.GetSection("Sessions"));
builder.Services.Configure<MobileOptions>(config.GetSection("Mobile"));
// Otp:FixedCode (default "12345" in appsettings.json) makes every one-time code that value, in every environment, and no SMS is needed.
// SECURITY: while it is set, anyone who knows a phone number can verify it and reset that account's password. Clear it once an SMS
// provider (ISmsSender) is registered below.
builder.Services.Configure<OtpOptions>(config.GetSection("Otp"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordService>();
if (config["Email:Provider"] == "SendGrid") builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
else builder.Services.AddScoped<IEmailSender, PickupEmailSender>();
// ponytail: no SMS provider yet. Outside Production the code is only logged; in Production, without Otp:FixedCode, phone codes answer
// 503 OTP_UNAVAILABLE until a real ISmsSender is registered here.
if (!builder.Environment.IsProduction()) builder.Services.AddSingleton<ISmsSender, LoggingSmsSender>();
builder.Services.AddApplicationServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IFileUrls, FileUrls>();
builder.Services.AddSingleton<AuctionSockets>();
builder.Services.AddHostedService<AuctionLifecycleWatcher>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
// Malformed JSON or a wrong field type reaches the exception handler (→ 422 VALIDATION_FAILED) instead of an empty 400.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<EnvelopeExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "eRecycleHub mobile API";
    doc.Info.Version = "v1";
    doc.Info.Description = "Contract: docs/API_REQUIREMENTS.md. Every response is { success, message, data } or { success: false, message, errorCode, errors }. " +
        "Send `Authorization: Bearer <token>` (from /api/v1/auth/login or /register) and `lang: en|ar`. Live bids: wss://<host>/ws/auctions/{id}.";
    doc.Servers = [new OpenApiServer { Url = "/", Description = "This host" }];
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" };
    doc.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] }];
    return Task.CompletedTask;
}));

const long MaxUpload = 20 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxUpload);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUpload);

builder.Services.AddAuthentication(SessionAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (c, _) =>
    {
        if (c.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) c.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString();
        await ErrorCodes.WriteAsync(c.HttpContext, 429, "RATE_LIMITED", "rate.limited");
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
    // Behind a reverse proxy (IIS / nginx) that sets X-Forwarded-For and X-Forwarded-Proto.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (args.Length > 0 && await Commands.RunAsync(app, args)) return;
if (app.Environment.IsProduction() && !string.IsNullOrEmpty(config["Otp:FixedCode"]))
    app.Logger.LogWarning("Otp:FixedCode is set: every one-time code is the same fixed value. Clear it once SMS delivery is configured.");

app.UseForwardedHeaders();
app.UseExceptionHandler();
// Bodiless framework answers (404 unknown route, 405, 415…) also get the envelope.
app.UseStatusCodePages(async c => await ErrorCodes.WriteAsync(c.HttpContext, c.HttpContext.Response.StatusCode,
    ErrorCodes.ForStatus(c.HttpContext.Response.StatusCode), null));
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(Security.HeadersAsync);
app.UseSwaggerUI(o =>
{
    o.RoutePrefix = "api/swagger";
    o.SwaggerEndpoint("/api/openapi/v1.json", "eRecycleHub mobile API v1");
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Json(new { status = "db.unavailable" }, statusCode: 503))
    .AllowAnonymous().ExcludeFromDescription();

// The app's SERVER setting is https://<host>/api/v1; the live socket is at the host root (it drops SERVER's path, docs M2).
var v1 = app.MapGroup("/api/v1").DisableAntiforgery();
v1.MapAuthEndpoints();
v1.MapCatalogEndpoints();
v1.MapAccountEndpoints();
v1.MapFileEndpoints();
AuctionSockets.Map(app);

app.Run();

public partial class Program;

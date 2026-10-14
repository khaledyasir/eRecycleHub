using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>Clock the tests can move. Defaults to real time.</summary>
public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}

/// <summary>Makes the next SaveChanges that adds a ledger entry fail, to prove financial operations roll back completely.</summary>
public sealed class LedgerFailure : SaveChangesInterceptor
{
    public volatile bool Armed;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Armed && e.Context!.ChangeTracker.Entries<WalletLedgerEntry>().Any(x => x.State == EntityState.Added))
        {
            Armed = false;
            throw new InvalidOperationException("Injected database failure");
        }
        return base.SavingChangesAsync(e, result, ct);
    }
}

/// <summary>
/// Hosts the real API against a throw-away SQL Server database (created from the migrations, then dev-seeded) on localhost.
/// One database per test run; tests create the rows they mutate so they stay independent.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public readonly string DatabaseName = "ERecycleHub_Test_" + Guid.NewGuid().ToString("N")[..10];
    public readonly string DataRoot = Path.Combine(Path.GetTempPath(), "erh-tests", Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    public LedgerFailure Failure { get; } = new();

    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ERH_TEST_SQL") is { Length: > 0 } custom
            ? custom.Replace("{db}", DatabaseName)
            : $"Server=localhost;Database={DatabaseName};Integrated Security=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        b.UseSetting("ConnectionStrings:Default", ConnectionString);
        b.UseSetting("Storage:Root", Path.Combine(DataRoot, "files"));
        b.UseSetting("Email:Provider", "Pickup");
        b.UseSetting("Email:PickupDirectory", Path.Combine(DataRoot, "mail"));
        b.UseSetting("RateLimits:AuthPerMinute", "100000");
        b.UseSetting("RateLimits:FormsPerMinute", "100000");
        b.UseSetting("RateLimits:BidsPerMinute", "100000");
        b.ConfigureServices(s =>
        {
            s.AddSingleton<TimeProvider>(Clock);
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(Failure));
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await ActivatorUtilities.CreateInstance<DevSeed>(scope.ServiceProvider).RunAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
        try { Directory.Delete(DataRoot, true); } catch (IOException) { }
    }

    public ApiClient Client() => new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }));

    public async Task<ApiClient> SignedInAsync(string email, string password)
    {
        var c = Client();
        var r = await c.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return c;
    }

    public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task DbAsync(Func<AppDbContext, Task> work) => DbAsync(async db => { await work(db); return 0; });

    /// <summary>Creates an active customer with a ledger-consistent wallet balance; returns (id, email). Password: <see cref="Password"/>.</summary>
    public const string Password = "TestPass#2026";

    public Task<(int id, string email)> NewCustomerAsync(decimal wallet = 1000, UserRole role = UserRole.Customer) => DbAsync(async db =>
    {
        var email = $"u{Guid.NewGuid():N}@test.local";
        var u = new User { FullName = "Test " + email[..6], Email = email, Role = role, CountryId = 1, PasswordHash = new PasswordService().Hash(Password), CreatedAtUtc = DateTime.UtcNow, WalletBalance = wallet };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        if (wallet != 0)
        {
            db.WalletLedger.Add(new WalletLedgerEntry { UserId = u.Id, Amount = wallet, BalanceAfter = wallet, Reason = LedgerReason.MigrationOpeningBalance, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        return (u.Id, email);
    });

    public Task<int> PublisherIdAsync(string email) => DbAsync(db => db.Publishers.Where(p => p.User!.Email == email).Select(p => p.Id).SingleAsync());

    public Task<int> NewAuctionAsync(int? publisherId = null, double startHours = -1, double endHours = 48, decimal start = 100, decimal increment = 10, decimal insurance = 0, bool active = true) =>
        DbAsync(async db =>
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var a = new Auction
            {
                RefNumber = "T" + Guid.NewGuid().ToString("N")[..10], CountryId = 1, PublisherId = publisherId, TitleEn = "Integration lot", TitleAr = "دفعة اختبار",
                StartPrice = start, CurrentPrice = start, BidIncrement = increment, InsuranceAmount = insurance, StartsAtUtc = now.AddHours(startHours),
                EndsAtUtc = now.AddHours(endHours), IsActive = active, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            db.Auctions.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>HTTP client that behaves like the web app: cookie session + antiforgery header on unsafe requests.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private string? _csrf;

    public HttpClient Http => http;

    public async Task<string> CsrfAsync()
    {
        if (_csrf is null)
        {
            var me = await http.GetFromJsonAsync<JsonElement>("/api/auth/me", Json);
            _csrf = me.GetProperty("csrfToken").GetString();
        }
        return _csrf!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, bool withCsrf = true)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        if (withCsrf) req.Headers.Add("X-CSRF-TOKEN", await CsrfAsync());
        var res = await http.SendAsync(req);
        // Sign-in state changes return a token bound to the new identity.
        if (res.IsSuccessStatusCode && url.StartsWith("/api/auth/") && res.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("csrfToken", out var t)) _csrf = t.GetString();
        }
        return res;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);
    public async Task<T> GetJsonAsync<T>(string url) => (await http.GetFromJsonAsync<T>(url, Json))!;
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null, bool withCsrf = true) =>
        SendAsync(HttpMethod.Post, url, body is null ? null : JsonContent.Create(body, options: Json), withCsrf);
    public Task<HttpResponseMessage> PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body is null ? null : JsonContent.Create(body, options: Json));
    public Task<HttpResponseMessage> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url, null);

    public Task<HttpResponseMessage> MultipartAsync(HttpMethod method, string url, object data, params (string name, string fileName, byte[] bytes)[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(data, Json)), "data" } };
        foreach (var (name, fileName, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, name, fileName);
        }
        return SendAsync(method, url, form);
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password, bool adminOnly = false) =>
        PostAsync("/api/auth/login", new { email, password, adminOnly });

    public Task<HttpResponseMessage> BidAsync(int auctionId, decimal amount) => PostAsync($"/api/auctions/{auctionId}/bids", new { amount });

    public static async Task<string?> CodeAsync(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }
}

public static class TestFiles
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    public static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
    public static readonly byte[] FakePngWithHtml = "<html><script>alert(1)</script></html>"u8.ToArray();
}

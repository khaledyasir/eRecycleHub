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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERecycleHub.IntegrationTests;

/// <summary>Stands in for Firebase: remembers every topic change and can report tokens as dead or fail.</summary>
public sealed class FakePushGateway : IPushGateway
{
    public sealed record Call(bool Subscribe, string Topic, string[] Tokens);

    private readonly List<Call> _calls = [];
    public HashSet<string> DeadTokens { get; } = [];
    public volatile bool Throw;

    public IReadOnlyList<Call> Calls { get { lock (_calls) return _calls.ToList(); } }

    public bool Did(bool subscribe, string topic, string token) => Calls.Any(c => c.Subscribe == subscribe && c.Topic == topic && c.Tokens.Contains(token));

    private Task<TopicResult> RecordAsync(bool subscribe, IReadOnlyList<string> tokens, string topic)
    {
        if (Throw) throw new InvalidOperationException("Injected push failure");
        lock (_calls) _calls.Add(new Call(subscribe, topic, tokens.ToArray()));
        return Task.FromResult(new TopicResult(tokens.Where(DeadTokens.Contains).ToList()));
    }

    public Task<TopicResult> SubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default) => RecordAsync(true, tokens, topic);
    public Task<TopicResult> UnsubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default) => RecordAsync(false, tokens, topic);

    private readonly List<PushTarget> _sent = [];
    private readonly List<(string Topic, PushMessage Message)> _topicSent = [];

    /// <summary>Every device message sent so far (token + message).</summary>
    public IReadOnlyList<PushTarget> Sent { get { lock (_sent) return _sent.ToList(); } }
    public IReadOnlyList<(string Topic, PushMessage Message)> TopicSent { get { lock (_topicSent) return _topicSent.ToList(); } }

    public Task<TopicResult> SendAsync(IReadOnlyList<PushTarget> targets, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("Injected push failure");
        lock (_sent) _sent.AddRange(targets);
        return Task.FromResult(new TopicResult(targets.Select(t => t.Token).Where(DeadTokens.Contains).Distinct().ToList()));
    }

    public Task SendToTopicAsync(string topic, PushMessage message, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("Injected push failure");
        lock (_topicSent) _topicSent.Add((topic, message));
        return Task.CompletedTask;
    }
}

/// <summary>The wallet-debit switch, flippable per test (it is a startup setting in production).</summary>
public sealed class TestBiddingSettings : IBiddingSettings
{
    public volatile bool On;
    public bool DebitWallet => On;
}

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
    /// <summary>Fails the next SaveChanges that adds an in-app notification (late in the bid transaction, after the bid row is written).</summary>
    public volatile bool ArmedOnNotification;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (ArmedOnNotification && e.Context!.ChangeTracker.Entries<Notification>().Any(x => x.State == EntityState.Added))
        {
            ArmedOnNotification = false;
            throw new InvalidOperationException("Injected database failure");
        }
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
    public FakePushGateway Push { get; } = new();
    public TestBiddingSettings Flags { get; } = new();

    /// <summary>Waits until the background queue (push subscriptions) has run everything queued so far.</summary>
    public async Task IdleAsync() => Assert.True(await Services.GetRequiredService<BackgroundWorkQueue>().WhenIdleAsync(TimeSpan.FromSeconds(10)), "background work did not finish");

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
        b.UseSetting("RateLimits:MoneyPerMinute", "100000");
        b.UseSetting("RateLimits:AdminMoneyPerMinute", "100000");
        b.UseSetting("Wallet:MaxPendingRequests", "100");   // the cap has its own tests; others create many requests per user
        b.UseSetting("Otp:FixedCode", "12345");
        b.UseSetting("AuctionEnd:Enabled", "false");   // tests close auctions explicitly; the timer must not touch their data
        b.ConfigureServices(s =>
        {
            s.AddSingleton<TimeProvider>(Clock);
            s.RemoveAll<IBiddingSettings>();
            s.AddSingleton<IBiddingSettings>(Flags);
            s.RemoveAll<IPushGateway>();
            s.AddSingleton<IPushGateway>(Push);
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

    public Task<(int id, string email)> NewCustomerAsync(decimal wallet = 1000) => DbAsync(async db =>
    {
        var email = $"u{Guid.NewGuid():N}@test.local";
        var u = new User { FullName = "Test " + email[..6], Email = email, Role = UserRole.Customer, CountryId = 1, PasswordHash = new PasswordService().Hash(Password), CreatedAtUtc = DateTime.UtcNow, WalletBalance = wallet };
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

/// <summary>HTTP client that behaves like the mobile app: Bearer token, lang header, JSON envelope.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public HttpClient Http => http;
    public string? Token { get; private set; }

    public void UseToken(string? token)
    {
        Token = token;
        http.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => http.PostAsync(url, JsonContent.Create(body ?? new { }, options: Json));
    public Task<HttpResponseMessage> PutAsync(string url, object body) => http.PutAsync(url, JsonContent.Create(body, options: Json));
    public Task<HttpResponseMessage> DeleteAsync(string url) => http.DeleteAsync(url);

    /// <summary>The envelope's <c>data</c>, after asserting success.</summary>
    public async Task<JsonElement> DataAsync(Task<HttpResponseMessage> call)
    {
        var res = await call;
        var body = await Body(res);
        Assert.True(res.IsSuccessStatusCode && body.GetProperty("success").GetBoolean(), $"{(int)res.StatusCode}: {body}");
        return body.GetProperty("data");
    }

    public static async Task<JsonElement> Body(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Asserts a failure envelope and returns its errorCode.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage r)
    {
        var body = await Body(r);
        Assert.False(body.GetProperty("success").GetBoolean());
        return body.GetProperty("errorCode").GetString();
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        var r = await PostAsync("/api/v1/auth/login", new { email, password });
        if (r.IsSuccessStatusCode) UseToken((await Body(r)).GetProperty("data").GetProperty("token").GetString());
        return r;
    }

    public Task<HttpResponseMessage> BidAsync(int auctionId, decimal amount) => PostAsync($"/api/v1/auctions/{auctionId}/bids", new { amount });
}

public static class TestFiles
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    public static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
    public static readonly byte[] FakePngWithHtml = "<html><script>alert(1)</script></html>"u8.ToArray();
}

using System.Net;
using System.Net.Http.Json;
using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>
/// Phase 6: authorization (admin-only routes), ownership of the caller's own wallet and notifications, input validation on money, rate limits on money endpoints,
/// and audit logging of admin money actions, including the ones that were refused.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityReviewTests(ApiFactory f)
{
    private static string Url(string pattern) => System.Text.RegularExpressions.Regex.Replace(pattern, @"\{[^}]+\}", "1");

    private Task<WalletTransactionDto> DbTransactionAsync(int id) => WalletAsync(w => w.GetAsync(id)!)!;

    private async Task<T> WalletAsync<T>(Func<WalletService, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<WalletService>());
    }

    private async Task<(int id, ApiClient client)> CustomerAsync(decimal wallet = 500)
    {
        var (id, email) = await f.NewCustomerAsync(wallet);
        return (id, await f.SignedInAsync(email, ApiFactory.Password));
    }

    private async Task<(Actor actor, ApiClient client)> AdminAsync(WebApplicationFactory<Program>? host = null)
    {
        var id = await f.DbAsync(db => db.Users.Where(u => u.Email == DevSeed.AdminEmail).Select(u => u.Id).SingleAsync());
        var http = (host ?? f).CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var client = new ApiClient(http);
        // Administrators cannot use the app's /api/v1/auth/login (it answers like a wrong password); they sign in on the website's token endpoint,
        // which, like every unsafe /api call without a Bearer token, needs the anonymous session's CSRF token.
        var csrf = (await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me")).GetProperty("csrfToken").GetString();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token") { Content = JsonContent.Create(new { email = DevSeed.AdminEmail, password = DevSeed.AdminPassword, adminOnly = true }) };
        login.Headers.Add("X-CSRF-TOKEN", csrf);
        var r = await http.SendAsync(login);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        client.UseToken((await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString());
        return (new Actor(id, UserRole.Admin), client);
    }

    private Task<List<AuditLog>> AuditAsync(string action, int entityId) =>
        f.DbAsync(db => db.AuditLogs.Where(l => l.Action == action && l.EntityType == nameof(WalletTransaction) && l.EntityId == entityId.ToString()).ToListAsync());

    private static Task<HttpResponseMessage> PostAmountAsync(ApiClient c, string url, decimal amount) => c.PostAsync(url, new { amount });

    private async Task<int> NewRequestAsync(ApiClient c, string kind, decimal amount)
    {
        Assert.True((await PostAmountAsync(c, $"/api/v1/wallet/{kind}", amount)).IsSuccessStatusCode);
        return await f.DbAsync(db => db.WalletTransactions.OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync());
    }

    // ---------- authorization ----------

    [Fact]
    public async Task Every_admin_route_refuses_customers_and_anonymous_callers()
    {
        var (_, customer) = await CustomerAsync();
        var anonymous = f.Client();
        var routes = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Path: Url(e.RoutePattern.RawText!),
                Multipart: e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
            .Distinct().ToList();
        Assert.True(routes.Count > 40, $"only {routes.Count} admin routes found: the scan is broken");

        foreach (var (method, path, multipart) in routes)
        {
            foreach (var (who, client, expected) in new[] { ("customer", customer, HttpStatusCode.Forbidden), ("anonymous", anonymous, HttpStatusCode.Unauthorized) })
            {
                // Upload routes only accept multipart: routing answers 415 to anything else before authorization runs.
                HttpContent? body = method is "GET" or "DELETE" ? null : multipart ? new MultipartFormDataContent { { new StringContent("{}"), "data" } } : JsonContent.Create(new { }, options: ApiClient.Json);
                var r = await client.Http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = body });
                Assert.True(r.StatusCode == expected, $"{who} {method} {path} returned {(int)r.StatusCode}, expected {(int)expected}");
            }
        }
    }

    [Fact]
    public async Task The_mobile_wallet_and_notification_endpoints_only_touch_the_callers_own_rows()
    {
        var (a, clientA) = await CustomerAsync();
        var (_, clientB) = await CustomerAsync(70);
        var tx = await NewRequestAsync(clientA, "withdraw", 10m);
        var note = await f.DbAsync(async db =>
        {
            var n = new Notification { UserId = a, Kind = NotificationKinds.TopUpApproved, Amount = 5, CreatedAtUtc = DateTime.UtcNow };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            return n.Id;
        });

        var theirs = await clientB.DataAsync(clientB.GetAsync("/api/v1/wallet"));
        Assert.Equal(0, theirs.GetProperty("transaction").GetArrayLength());
        Assert.Equal(0, (await clientB.DataAsync(clientB.GetAsync("/api/v1/wallet/ledger"))).GetProperty("items").EnumerateArray().Count(i => i.TryGetProperty("referenceId", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number && r.GetInt32() == tx));
        Assert.Equal(0, (await clientB.DataAsync(clientB.GetAsync("/api/v1/notifications"))).GetArrayLength());

        await clientB.PostAsync("/api/v1/notifications/read", new { ids = new[] { note } });
        await clientB.DeleteAsync($"/api/v1/notifications/{note}");
        var row = await f.DbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.Id == note));
        Assert.False(row.IsRead);
        Assert.Null(row.DeletedAtUtc);
    }

    // ---------- input validation ----------

    [Fact]
    public async Task Money_inputs_reject_zero_negative_too_many_decimals_and_too_large_amounts()
    {
        var (id, client) = await CustomerAsync(500);
        var before = await f.DbAsync(db => db.WalletTransactions.CountAsync(t => t.UserId == id));
        foreach (var bad in new[] { 0m, -5m, 10.001m, 1_000_000.01m })
            foreach (var kind in new[] { "increase", "withdraw" })
                Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAmountAsync(client, $"/api/v1/wallet/{kind}", bad)).StatusCode);   // VALIDATION_FAILED on the mobile API
        Assert.Equal(before, await f.DbAsync(db => db.WalletTransactions.CountAsync(t => t.UserId == id)));

        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        foreach (var bad in new[] { 0m, -1m, 110.005m })
        {
            var status = (int)(await client.BidAsync(auction, bad)).StatusCode;
            Assert.InRange(status, 400, 499);
        }
        Assert.Equal(0, await f.DbAsync(db => db.Bids.CountAsync(b => b.AuctionId == auction)));

        var (admin, _) = await AdminAsync();
        foreach (var bad in new[] { 0m, -1m, 0.001m, 1_000_000.01m })
        {
            var ex = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, bad, null, null, null)));
            Assert.Equal("wallet.invalid_amount", ex.Code);
        }
        Assert.Equal(500m, await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.WalletBalance).SingleAsync()));
    }

    [Fact]
    public async Task Paging_and_header_inputs_are_bounded_and_never_cause_a_server_error()
    {
        var (_, client) = await CustomerAsync();
        foreach (var url in new[] { "/api/v1/wallet/ledger?page=-3&pageSize=100000", "/api/v1/wallet/ledger?page=0&pageSize=-1", "/api/v1/notifications?page=0&pageSize=-1", "/api/v1/notifications?page=99999999&pageSize=100000" })
            Assert.InRange((int)(await client.GetAsync(url)).StatusCode, 200, 499);
        var ledger = await client.DataAsync(client.GetAsync("/api/v1/wallet/ledger?page=1&pageSize=100000"));
        Assert.True(ledger.GetProperty("pageSize").GetInt32() <= 100);
    }

    // ---------- rate limits ----------

    [Fact]
    public async Task Wallet_requests_are_limited_per_user_and_the_limit_is_configurable()
    {
        using var host = f.WithWebHostBuilder(b => b.UseSetting("RateLimits:MoneyPerMinute", "3"));
        async Task<ApiClient> SignIn(string email)
        {
            var c = new ApiClient(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }));
            Assert.Equal(HttpStatusCode.OK, (await c.LoginAsync(email, ApiFactory.Password)).StatusCode);
            return c;
        }
        var first = await SignIn((await f.NewCustomerAsync(500)).email);
        var second = await SignIn((await f.NewCustomerAsync(500)).email);

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await PostAmountAsync(first, "/api/v1/wallet/withdraw", 1m)).StatusCode);
        var limited = await PostAmountAsync(first, "/api/v1/wallet/withdraw", 1m);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("RATE_LIMITED", await ApiClient.ErrorCodeAsync(limited));
        Assert.Equal(HttpStatusCode.OK, (await PostAmountAsync(second, "/api/v1/wallet/increase", 1m)).StatusCode);   // another user is not affected
    }

    [Fact]
    public async Task Administrator_money_actions_are_limited_per_administrator()
    {
        using var host = f.WithWebHostBuilder(b => b.UseSetting("RateLimits:AdminMoneyPerMinute", "2"));
        var (_, admin) = await AdminAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/transactions/2147000000/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/transactions/2147000000/reject")).StatusCode);
        var limited = await admin.PostAsync("/api/admin/transactions/2147000000/reverse");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("RATE_LIMITED", await ApiClient.ErrorCodeAsync(limited));
    }

    // ---------- audit logging ----------

    [Fact]
    public async Task Every_admin_money_action_leaves_one_audit_row_with_the_admin_the_target_and_the_amount()
    {
        var (admin, _) = await AdminAsync();
        var (userId, client) = await CustomerAsync(0);
        var credit = (await WalletAsync(w => w.CreateApprovedCreditAsync(admin, userId, 25m, null, null, "test"))).Id;
        var topUp = await NewRequestAsync(client, "increase", 40m);
        var rejected = await NewRequestAsync(client, "increase", 41m);
        var deleted = await NewRequestAsync(client, "increase", 42m);
        await WalletAsync(w => w.ApproveAsync(admin, topUp));
        var withdrawal = await NewRequestAsync(client, "withdraw", 5m);
        await WalletAsync(w => w.ApproveAsync(admin, withdrawal));
        await WalletAsync(w => w.RejectAsync(admin, rejected));
        await WalletAsync(async w => { await w.DeleteAsync(admin, deleted); return 0; });
        await WalletAsync(w => w.ReverseAsync(admin, credit));

        foreach (var (action, id, amount) in new[] { ("wallet.credit", credit, "25"), ("wallet.approved", topUp, "40"), ("wallet.approved", withdrawal, "5"), ("wallet.rejected", rejected, "41"), ("wallet.reversed", credit, "25") })
        {
            var rows = await AuditAsync(action, id);
            var row = Assert.Single(rows);
            Assert.Equal(admin.UserId, row.ActorUserId);
            Assert.Contains(amount, row.Data);
            Assert.Contains(userId.ToString(), row.Data);
        }
        Assert.Equal(admin.UserId, Assert.Single(await AuditAsync("wallet.delete", deleted)).ActorUserId);
    }

    [Fact]
    public async Task A_refused_money_decision_is_audited_and_changes_nothing()
    {
        var (admin, _) = await AdminAsync();
        var (userId, client) = await CustomerAsync(100);
        var tx = await NewRequestAsync(client, "withdraw", 80m);
        await f.DbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 50m)));   // no longer covers it

        var ex = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ApproveAsync(admin, tx)));

        Assert.Equal("wallet.insufficient", ex.Code);
        var row = Assert.Single(await AuditAsync("wallet.approve_refused", tx));
        Assert.Equal(admin.UserId, row.ActorUserId);
        Assert.Contains("insufficient", row.Data);
        Assert.Empty(await AuditAsync("wallet.approved", tx));
        Assert.Equal(WalletTransactionStatus.Pending, (await DbTransactionAsync(tx)).Status);
        Assert.Equal(50m, await f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync()));
    }

    // ---------- separation of duties ----------

    [Fact]
    public async Task An_administrator_cannot_approve_their_own_request_but_can_still_reject_it_and_credit_wallets()
    {
        var (admin, _) = await AdminAsync();
        var own = (await WalletAsync(w => w.RequestTopUpAsync(admin.UserId, 12m, null, null))).Id;   // admins have no app login; their own request comes from the service
        var before = await f.DbAsync(db => db.Users.Where(u => u.Id == admin.UserId).Select(u => u.WalletBalance).SingleAsync());

        var ex = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ApproveAsync(admin, own)));

        Assert.Equal("wallet.self_approval_not_allowed", ex.Code);
        Assert.Equal(403, ex.Status);
        Assert.Equal(WalletTransactionStatus.Pending, (await DbTransactionAsync(own)).Status);
        Assert.Equal(before, await f.DbAsync(db => db.Users.Where(u => u.Id == admin.UserId).Select(u => u.WalletBalance).SingleAsync()));
        Assert.Contains("self_approval", Assert.Single(await AuditAsync("wallet.approve_refused", own)).Data);

        await WalletAsync(w => w.RejectAsync(admin, own));   // declining one's own request moves no money, so it stays possible
        Assert.Equal(WalletTransactionStatus.Rejected, (await DbTransactionAsync(own)).Status);

        var (other, _) = await CustomerAsync(0);
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, other, 10m, null, null, null));   // admin credit for a customer is unaffected
    }
}

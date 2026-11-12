using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERecycleHub.IntegrationTests;

/// <summary>The website's wallet endpoints (cookie session, CSRF) show the same figures and withdrawal rules as the app's /api/v1/wallet.</summary>
[Collection(ApiCollection.Name)]
public class WebWalletTests(ApiFactory f)
{
    private static decimal Num(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    /// <summary>Signs in like the browser: the anonymous session's CSRF token, then the cookie login. Returns the client and the new CSRF token.</summary>
    private async Task<(HttpClient http, string csrf)> WebSignInAsync(string email)
    {
        var http = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var anon = await http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password = ApiFactory.Password }) };
        login.Headers.Add("X-CSRF-TOKEN", anon.GetProperty("csrfToken").GetString());
        var r = await http.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (http, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string csrf, string url, object body)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        m.Headers.Add("X-CSRF-TOKEN", csrf);
        return http.SendAsync(m);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task Withdrawal_request_counts_against_available_and_cannot_exceed_it()
    {
        var (_, email) = await f.NewCustomerAsync(wallet: 1000);
        var (http, csrf) = await WebSignInAsync(email);

        var key = Guid.NewGuid().ToString();
        var r = await PostAsync(http, csrf, "/api/account/wallet/withdrawals", new { amount = 300m, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var tx = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Withdrawal", tx.GetProperty("type").GetString());
        Assert.Equal("Pending", tx.GetProperty("status").GetString());
        // A retry with the same key returns the same request.
        var again = await (await PostAsync(http, csrf, "/api/account/wallet/withdrawals", new { amount = 300m, idempotencyKey = key })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tx.GetProperty("id").GetInt32(), again.GetProperty("id").GetInt32());

        var w = await http.GetFromJsonAsync<JsonElement>("/api/account/wallet");
        Assert.Equal(1000m, Num(w, "balance"));
        Assert.Equal(300m, Num(w, "pendingWithdrawals"));
        Assert.Equal(700m, Num(w, "available"));
        Assert.Equal(0, w.GetProperty("pendingRequests").GetInt32()); // top-ups only

        Assert.Equal("wallet.insufficient", await CodeAsync(await PostAsync(http, csrf, "/api/account/wallet/withdrawals", new { amount = 700.01m })));
    }

    private static Task<HttpResponseMessage> DeleteProfileAsync(HttpClient http, string csrf)
    {
        var m = new HttpRequestMessage(HttpMethod.Delete, "/api/account/profile");
        m.Headers.Add("X-CSRF-TOKEN", csrf);
        return http.SendAsync(m);
    }

    [Fact]
    public async Task Delete_account_is_blocked_with_wallet_money_and_ends_the_session_when_empty()
    {
        var (_, rich) = await f.NewCustomerAsync(wallet: 50);
        var (http, csrf) = await WebSignInAsync(rich);
        var blocked = await DeleteProfileAsync(http, csrf);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("account.delete_blocked", await CodeAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/account/profile")).StatusCode); // still signed in

        var (id, email) = await f.NewCustomerAsync(wallet: 0);
        (http, csrf) = await WebSignInAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await DeleteProfileAsync(http, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/account/profile")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await http.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("user").ValueKind);
        Assert.True(await f.DbAsync(async db => (await db.Users.FindAsync(id))!.IsDeleted));
    }
}

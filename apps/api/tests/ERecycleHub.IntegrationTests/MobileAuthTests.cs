using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>The mobile app: Bearer tokens, no cookies, no CSRF token; phone OTP password reset with the configured fixed code.</summary>
[Collection(ApiCollection.Name)]
public class MobileAuthTests(ApiFactory f)
{
    private HttpClient Mobile(string? token = null)
    {
        var http = f.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        http.DefaultRequestHeaders.Add("X-Client", "mobile");
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<(string access, string refresh)> TokensAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.True(body.GetProperty("expiresIn").GetInt64() > 0);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    [Fact]
    public async Task Token_sign_in_works_without_cookies_or_csrf_and_ends_when_the_password_changes()
    {
        var (_, email) = await f.NewCustomerAsync();
        var (access, refresh) = await TokensAsync(await Mobile().PostAsJsonAsync("/api/auth/token", new { email, password = ApiFactory.Password }));

        var me = await Mobile(access).GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(email, me.GetProperty("user").GetProperty("email").GetString());
        var auction = await f.NewAuctionAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await Mobile(access).PutAsync($"/api/auctions/{auction}/favorite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mobile("not-a-token").GetAsync("/api/account/favorites")).StatusCode);

        var (access2, refresh2) = await TokensAsync(await Mobile().PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = refresh }));
        Assert.Equal(HttpStatusCode.OK, (await Mobile(access2).GetAsync("/api/account/favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mobile().PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = "garbage" })).StatusCode);

        // A password change elsewhere (new security stamp) ends every token at once.
        await f.DbAsync(db => db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, "changed")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mobile(access2).GetAsync("/api/account/favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mobile().PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken = refresh2 })).StatusCode);
    }

    [Fact]
    public async Task Browser_requests_without_the_client_header_still_need_the_csrf_token()
    {
        var (_, email) = await f.NewCustomerAsync();
        var c = f.Client();
        var r = await c.PostAsync("/api/auth/login", new { email, password = ApiFactory.Password }, withCsrf: false);
        Assert.Equal("csrf.invalid", await ApiClient.CodeAsync(r));
    }

    [Fact]
    public async Task Phone_otp_resets_the_password()
    {
        var (id, email) = await f.NewCustomerAsync();
        var phone = "07" + Random.Shared.Next(10_000_000, 99_999_999);
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Phone, phone)));

        Assert.Equal(HttpStatusCode.NoContent, (await Mobile().PostAsJsonAsync("/api/auth/otp/send", new { phone })).StatusCode);
        Assert.Equal("otp.invalid", await ApiClient.CodeAsync(await Mobile().PostAsJsonAsync("/api/auth/otp/verify", new { phone, code = "000000" })));
        Assert.Equal("otp.invalid", await ApiClient.CodeAsync(await Mobile().PostAsJsonAsync("/api/auth/otp/verify", new { phone = "0799999999", code = "123456" })));

        var verify = await Mobile().PostAsJsonAsync("/api/auth/otp/verify", new { phone, code = "123456" });
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resetToken").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await Mobile().PostAsJsonAsync("/api/auth/reset-password", new { token, password = "NewPass#2026" })).StatusCode);
        await TokensAsync(await Mobile().PostAsJsonAsync("/api/auth/token", new { email, password = "NewPass#2026" }));
    }
}

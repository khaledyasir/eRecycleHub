using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERecycleHub.IntegrationTests;

/// <summary>App sessions end when the account's security stamp rotates, whichever code path rotated it.</summary>
[Collection(ApiCollection.Name)]
public class SessionStampTests(ApiFactory f)
{
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

    [Fact]
    public async Task A_password_change_on_the_website_ends_the_app_sessions_on_app_and_web_routes()
    {
        var (_, email) = await f.NewCustomerAsync();
        var app = await f.SignedInAsync(email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/account/profile")).StatusCode);

        var (web, csrf) = await WebSignInAsync(email);
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password") { Content = JsonContent.Create(new { currentPassword = ApiFactory.Password, newPassword = "Changed#2026" }) };
        change.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.OK, (await web.SendAsync(change)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/account/profile")).StatusCode);
    }

    [Fact]
    public async Task A_password_change_in_the_app_keeps_this_device_and_ends_the_others()
    {
        var (_, email) = await f.NewCustomerAsync();
        var thisDevice = await f.SignedInAsync(email, ApiFactory.Password);
        var otherDevice = await f.SignedInAsync(email, ApiFactory.Password);

        await thisDevice.DataAsync(thisDevice.PostAsync("/api/v1/profile/password", new { currentPassword = ApiFactory.Password, newPassword = "Changed#2026" }));

        Assert.Equal(HttpStatusCode.OK, (await thisDevice.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client().LoginAsync(email, "Changed#2026")).StatusCode);
    }
}

using System.Net;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFactory f)
{
    [Fact]
    public async Task Unsafe_requests_without_a_csrf_token_are_rejected()
    {
        var c = await f.SignedInAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword);
        var r = await c.PostAsync($"/api/auctions/{await f.NewAuctionAsync()}/bids", new { amount = 500 }, withCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("csrf.invalid", await ApiClient.CodeAsync(r));
    }

    [Fact]
    public async Task Csrf_token_of_another_session_is_rejected()
    {
        var a = await f.SignedInAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword);
        var b = await f.SignedInAsync(DevSeed.Customer2Email, DevSeed.CustomerPassword);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/notifications/read-all");
        req.Headers.Add("X-CSRF-TOKEN", await a.CsrfAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Http.SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/admin/transactions")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/seller/dashboard")]
    [InlineData("/api/account/dashboard")]
    [InlineData("/api/account/wallet")]
    public async Task Anonymous_gets_401_on_protected_endpoints(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/audit")]
    [InlineData("/api/seller/dashboard")]
    [InlineData("/api/seller/auctions")]
    public async Task Customer_gets_403_on_admin_and_seller_endpoints(string url)
    {
        var c = await f.SignedInAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Publisher_gets_403_on_admin_endpoints()
    {
        var p = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.PostAsync("/api/admin/transactions/1/approve")).StatusCode);
    }

    [Fact]
    public async Task Login_failure_is_generic_and_admin_login_refuses_other_roles()
    {
        var c = f.Client();
        var unknown = await c.LoginAsync("nobody@nowhere.test", "whatever-123");
        var wrong = await c.LoginAsync(DevSeed.CustomerEmail, "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await ApiClient.CodeAsync(unknown), await ApiClient.CodeAsync(wrong));
        Assert.Equal("auth.invalid_credentials", await ApiClient.CodeAsync(await c.LoginAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword, adminOnly: true)));
    }

    [Fact]
    public async Task Account_locks_after_five_failures_and_lock_is_persistent()
    {
        var (id, email) = await f.NewCustomerAsync();
        var c = f.Client();
        for (var i = 0; i < 5; i++) await c.LoginAsync(email, "bad-password-" + i);
        var locked = await c.LoginAsync(email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("auth.locked", await ApiClient.CodeAsync(locked));
        Assert.NotNull(await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.LockoutEndUtc).SingleAsync()));
    }

    [Fact]
    public async Task Legacy_v1_hash_is_upgraded_on_first_login()
    {
        var (id, email) = await f.NewCustomerAsync();
        const string legacy = "v1.100000.YGM7pws5plHrwRMAcCEt/A==.d9d8i6xIXDGB/Qd3vomNM/6mzCLHtYx3OttCgq63V8U="; // "AdminPass#2026" from the legacy dev DB
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, legacy)));
        Assert.Equal(HttpStatusCode.OK, (await f.Client().LoginAsync(email, "AdminPass#2026")).StatusCode);
        var hash = await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.PasswordHash).SingleAsync());
        Assert.False(PasswordService.IsLegacyV1(hash!));
        Assert.Equal(HttpStatusCode.OK, (await f.Client().LoginAsync(email, "AdminPass#2026")).StatusCode);
    }

    [Fact]
    public async Task Changing_password_ends_other_sessions_but_keeps_the_current_one()
    {
        var (_, email) = await f.NewCustomerAsync();
        var first = await f.SignedInAsync(email, ApiFactory.Password);
        var second = await f.SignedInAsync(email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync("/api/auth/change-password", new { currentPassword = ApiFactory.Password, newPassword = "NewPassword#2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/account/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/account/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_session_stops_working_immediately()
    {
        var (id, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/active", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/account/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_does_not_reveal_accounts_and_tokens_work_once()
    {
        var c = f.Client();
        var known = await c.PostAsync("/api/auth/forgot-password", new { email = DevSeed.Customer2Email });
        var unknown = await c.PostAsync("/api/auth/forgot-password", new { email = "nobody@nowhere.test" });
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        var mail = Directory.GetFiles(Path.Combine(f.DataRoot, "mail")).Select(File.ReadAllText).Last(m => m.Contains(DevSeed.Customer2Email));
        var token = Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(mail, "token=([^\"<&]+)").Groups[1].Value);
        Assert.True((await c.GetJsonAsync<JsonElement>($"/api/auth/reset-password/validate?token={Uri.EscapeDataString(token)}")).GetProperty("valid").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/reset-password", new { token, password = DevSeed.CustomerPassword })).StatusCode);
        Assert.Equal("auth.reset_token_invalid", await ApiClient.CodeAsync(await c.PostAsync("/api/auth/reset-password", new { token, password = "Another#Pass2026" })));
        Assert.Equal("auth.reset_token_invalid", await ApiClient.CodeAsync(await c.PostAsync("/api/auth/reset-password", new { token = "forged", password = "Another#Pass2026" })));
    }

    [Fact]
    public async Task Profile_update_cannot_change_wallet_role_email_or_status()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 525);
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var r = await c.MultipartAsync(HttpMethod.Put, "/api/account/profile",
            new { fullName = "Renamed Customer", countryId = 1, walletBalance = 99999, role = "Admin", isActive = false, email = "evil@test.local" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var u = await f.DbAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal("Renamed Customer", u.FullName);
        Assert.Equal(525m, u.WalletBalance);
        Assert.Equal(UserRole.Customer, u.Role);
        Assert.Equal(email, u.Email);
        Assert.True(u.IsActive);
    }

    [Fact]
    public async Task Private_documents_are_only_served_to_owner_and_admin()
    {
        var (id, email) = await f.NewCustomerAsync();
        var owner = await f.SignedInAsync(email, ApiFactory.Password);
        await owner.MultipartAsync(HttpMethod.Put, "/api/account/profile", new { fullName = "Doc Owner", countryId = 1 }, ("idDocument", "id.pdf", TestFiles.Pdf));
        var fileId = await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.IdDocumentFileId).SingleAsync());
        Assert.NotNull(fileId);
        var url = $"/api/files/{fileId}";

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await f.SignedInAsync(DevSeed.Customer2Email, DevSeed.CustomerPassword)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword)).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Uploads_with_wrong_content_are_rejected()
    {
        var c = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        var r = await c.MultipartAsync(HttpMethod.Put, "/api/account/profile", new { fullName = "X", countryId = 1 }, ("avatar", "evil.png", TestFiles.FakePngWithHtml));
        Assert.Equal("file.content_mismatch", await ApiClient.CodeAsync(r));
        var svg = await c.MultipartAsync(HttpMethod.Put, "/api/account/profile", new { fullName = "X", countryId = 1 }, ("avatar", "x.svg", "<svg onload=alert(1)/>"u8.ToArray()));
        Assert.Equal("file.type_not_allowed", await ApiClient.CodeAsync(svg));
    }

    [Fact]
    public async Task Notifications_of_other_users_cannot_be_read_or_deleted()
    {
        var victimId = await f.DbAsync(db => db.Users.Where(u => u.Email == DevSeed.CustomerEmail).Select(u => u.Id).SingleAsync());
        var nid = await f.DbAsync(async db =>
        {
            var n = new Notification { UserId = victimId, Kind = NotificationKinds.Legacy, Title = "private", CreatedAtUtc = DateTime.UtcNow };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            return n.Id;
        });
        var attacker = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/account/notifications/{nid}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.DeleteAsync($"/api/account/notifications/{nid}")).StatusCode);
        Assert.False(await f.DbAsync(db => db.Notifications.Where(n => n.Id == nid).Select(n => n.IsRead).SingleAsync()));
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        var r = await f.Client().GetAsync("/api/countries");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
    }
}

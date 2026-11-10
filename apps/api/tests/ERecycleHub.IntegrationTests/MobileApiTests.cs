using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>The mobile API contract (docs/API_REQUIREMENTS.md): paths, envelope, key names and error codes.</summary>
[Collection(ApiCollection.Name)]
public class MobileApiTests(ApiFactory f)
{
    private const string Otp = "12345"; // Otp:FixedCode in ApiFactory

    private static string NewPhone() => "079" + Random.Shared.Next(1_000_000, 9_999_999);

    private async Task<ApiClient> CustomerAsync(decimal wallet = 1000) =>
        await f.SignedInAsync((await f.NewCustomerAsync(wallet)).email, ApiFactory.Password);

    // ---------- envelope & auth ----------

    [Fact]
    public async Task Protected_paths_answer_401_UNAUTHORIZED_envelope_and_unknown_routes_404()
    {
        var c = f.Client();
        var r = await c.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ApiClient.ErrorCodeAsync(r));

        c.UseToken("not-a-real-token");
        Assert.Equal("UNAUTHORIZED", await ApiClient.ErrorCodeAsync(await c.GetAsync("/api/v1/wallet")));
        var signedIn = await CustomerAsync();
        Assert.Equal("NOT_FOUND", await ApiClient.ErrorCodeAsync(await signedIn.GetAsync("/api/v1/does-not-exist")));
    }

    [Fact]
    public async Task Login_returns_token_and_expiry_and_wrong_password_is_INVALID_CREDENTIALS()
    {
        var (_, email) = await f.NewCustomerAsync();
        var c = f.Client();
        var bad = await c.LoginAsync(email, "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ApiClient.ErrorCodeAsync(bad));

        var data = await c.DataAsync(c.PostAsync("/api/v1/auth/login", new { email, password = ApiFactory.Password }));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("token").GetString()));
        Assert.True(data.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddDays(20));
    }

    [Fact]
    public async Task Malformed_json_is_VALIDATION_FAILED()
    {
        var c = f.Client();
        var r = await c.Http.PostAsync("/api/v1/auth/login", new StringContent("{not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ApiClient.ErrorCodeAsync(r));
    }

    [Fact]
    public async Task Register_after_otp_verify_signs_in_and_returns_E164_phone()
    {
        var c = f.Client();
        var phone = "+962" + NewPhone()[1..];
        var email = $"m{Guid.NewGuid():N}@test.local";
        var body = new { type = "Customer", countryId = 1, fullName = "Mobile User", phone, email, password = "Secret#123" };

        Assert.Equal("OTP_NOT_VERIFIED", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/auth/register", body)));

        await c.DataAsync(c.PostAsync("/api/v1/auth/otp/send", new { phone }));
        Assert.Equal("INVALID_OTP", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = "00000" })));
        var verified = await c.DataAsync(c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = Otp }));
        Assert.Equal(JsonValueKind.Null, verified.GetProperty("resetToken").ValueKind); // no account yet

        var token = await c.DataAsync(c.PostAsync("/api/v1/auth/register", body));
        c.UseToken(token.GetProperty("token").GetString());
        var profile = await c.DataAsync(c.GetAsync("/api/v1/profile"));
        Assert.Equal(phone, profile.GetProperty("phone").GetString());
        Assert.Equal(email, profile.GetProperty("email").GetString());

        // The verified code was used up; the same e-mail is now taken.
        var again = await c.PostAsync("/api/v1/auth/register", body);
        Assert.Equal("VALIDATION_FAILED", await ApiClient.ErrorCodeAsync(again));
        Assert.True((await ApiClient.Body(again)).GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Password_reset_by_phone_ends_old_sessions()
    {
        var (id, email) = await f.NewCustomerAsync();
        var local = NewPhone();
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Phone, local)));
        var signedIn = await f.SignedInAsync(email, ApiFactory.Password);

        var c = f.Client();
        var phone = "+962" + local[1..];
        await c.DataAsync(c.PostAsync("/api/v1/auth/otp/send", new { phone }));
        var resetToken = (await c.DataAsync(c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = Otp }))).GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(resetToken));
        await c.DataAsync(c.PostAsync("/api/v1/auth/password/reset", new { phone, password = "BrandNew#2026", resetToken }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client().LoginAsync(email, "BrandNew#2026")).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_its_push_token()
    {
        var c = await CustomerAsync();
        var fcm = "fcm-" + Guid.NewGuid();
        await c.DataAsync(c.PostAsync("/api/v1/devices/fcm-token", new { token = fcm }));
        await c.DataAsync(c.PostAsync("/api/v1/devices/fcm-token", new { token = fcm })); // re-sent on every sign-in: fine
        Assert.Equal(1, await f.DbAsync(db => db.DeviceTokens.CountAsync(d => d.Token == fcm)));

        await c.DataAsync(c.PostAsync("/api/v1/auth/logout"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(0, await f.DbAsync(db => db.DeviceTokens.CountAsync(d => d.Token == fcm)));
    }

    // ---------- content ----------

    [Fact]
    public async Task Countries_and_contents_are_public_with_fixed_content_ids()
    {
        var c = f.Client();
        var countries = await c.DataAsync(c.GetAsync("/api/v1/countries"));
        Assert.Contains(countries.EnumerateArray(), x => x.GetProperty("code").GetString() == "JO" && x.TryGetProperty("nameAr", out _));
        var ids = (await c.DataAsync(c.GetAsync("/api/v1/contents"))).EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToList();
        Assert.Equal([1, 2, 3, 4], ids);

        var signedIn = await CustomerAsync();
        await signedIn.DataAsync(signedIn.GetAsync("/api/v1/faqs"));
        await signedIn.DataAsync(signedIn.GetAsync("/api/v1/contact-us"));
        var slide = (await signedIn.DataAsync(signedIn.GetAsync("/api/v1/home/slides")))[0];
        Assert.StartsWith("https://localhost/api/v1/files/", slide.GetProperty("image").GetString());
        Assert.Equal("أعد التدوير بذكاء", slide.GetProperty("titleAr").GetString());
    }

    [Fact]
    public async Task Auction_lists_use_the_app_key_names_and_statuses_start_with_All()
    {
        var c = await CustomerAsync();
        var first = (await c.DataAsync(c.GetAsync("/api/v1/auctions")))[0];
        foreach (var key in new[] { "id", "categoryId", "titleEn", "titleAr", "mainImage", "images", "currentPrice", "increaseRange", "endDate", "currency", "bidsCount", "isexpire" })
            Assert.True(first.TryGetProperty(key, out _), key);
        Assert.Equal(JsonValueKind.Array, first.GetProperty("images").ValueKind);

        var statuses = (await c.DataAsync(c.GetAsync("/api/v1/auctions/statuses"))).EnumerateArray().ToList();
        Assert.Equal([4, 1, 2, 3], statuses.Select(s => s.GetProperty("id").GetInt32()));
        Assert.Equal(statuses[0].GetProperty("auctionCount").GetInt32(), statuses.Skip(1).Sum(s => s.GetProperty("auctionCount").GetInt32()));

        var cats = await c.DataAsync(c.GetAsync("/api/v1/categories"));
        Assert.True(cats.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Details_embed_the_users_wallet_and_keep_the_typo_keys()
    {
        var c = await CustomerAsync(wallet: 321);
        var id = await f.NewAuctionAsync(insurance: 50);
        var d = await c.DataAsync(c.GetAsync($"/api/v1/auctions/{id}"));
        Assert.Equal(321m, d.GetProperty("wallet").GetDecimal());
        Assert.Equal(50m, d.GetProperty("insuranceAmount").GetDecimal());
        Assert.True(d.TryGetProperty("termAndConditon", out _));
        Assert.False(d.GetProperty("isexpire").GetBoolean());
        Assert.Equal("Active", d.GetProperty("status").GetString());
        Assert.Equal("NOT_FOUND", await ApiClient.ErrorCodeAsync(await c.GetAsync("/api/v1/auctions/999999999")));
    }

    // ---------- bidding ----------

    [Fact]
    public async Task Bid_returns_full_details_and_the_outbid_user_is_notified()
    {
        var id = await f.NewAuctionAsync(start: 100, increment: 10);
        var (firstId, firstEmail) = await f.NewCustomerAsync();
        var first = await f.SignedInAsync(firstEmail, ApiFactory.Password);
        var second = await CustomerAsync();

        var d = await first.DataAsync(first.BidAsync(id, 110));
        Assert.Equal(110m, d.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(firstId, d.GetProperty("latestBids")[0].GetProperty("userId").GetInt32());

        await second.DataAsync(second.BidAsync(id, 120));
        var notes = await first.DataAsync(first.GetAsync("/api/v1/notifications"));
        Assert.Contains(notes.EnumerateArray(), n => n.GetProperty("type").GetString() == "bid" && n.GetProperty("titleEn").GetString() == "You were outbid");

        // first still believes the price is 110 and bids the minimum it computed: someone was faster.
        Assert.Equal("OUTBID", await ApiClient.ErrorCodeAsync(await first.BidAsync(id, 120)));
        Assert.Equal("BID_TOO_LOW", await ApiClient.ErrorCodeAsync(await first.BidAsync(id, 125)));
    }

    [Fact]
    public async Task Bid_rule_errors_map_to_app_codes()
    {
        var c = await CustomerAsync(wallet: 1000);
        var ended = await f.NewAuctionAsync(startHours: -48, endHours: -1);
        var upcoming = await f.NewAuctionAsync(startHours: 24, endHours: 48);
        var deposit = await f.NewAuctionAsync(insurance: 5000);
        Assert.Equal("AUCTION_CLOSED", await ApiClient.ErrorCodeAsync(await c.BidAsync(ended, 1000)));
        Assert.Equal("AUCTION_NOT_STARTED", await ApiClient.ErrorCodeAsync(await c.BidAsync(upcoming, 1000)));
        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await c.BidAsync(deposit, 1000)));
        Assert.Equal("VALIDATION_FAILED", await ApiClient.ErrorCodeAsync(await c.BidAsync(deposit, -5)));
    }

    [Fact]
    public async Task Error_messages_are_readable_in_the_requested_language()
    {
        var c = await CustomerAsync();
        var id = await f.NewAuctionAsync(start: 100, increment: 10);

        var low = await ApiClient.Body(await c.BidAsync(id, 105));
        Assert.Equal("BID_TOO_LOW", low.GetProperty("errorCode").GetString());
        Assert.Equal("Your bid must be at least 110.", low.GetProperty("message").GetString());

        c.Http.DefaultRequestHeaders.Add("lang", "ar");
        var bad = await ApiClient.Body(await c.PutAsync("/api/v1/profile", new { fullName = "X", email = "not-an-email", phone = "" }));
        Assert.Equal("VALIDATION_FAILED", bad.GetProperty("errorCode").GetString());
        Assert.Equal("يرجى إدخال بريد إلكتروني صحيح.", bad.GetProperty("message").GetString());
        Assert.Equal("يرجى إدخال بريد إلكتروني صحيح.", bad.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Theory]
    [InlineData("api/ws")]
    [InlineData("ws")]
    public async Task Websocket_listeners_receive_bid_placed_with_the_same_bid_id(string prefix)
    {
        var id = await f.NewAuctionAsync(start: 100, increment: 10);
        var bidder = await CustomerAsync();
        var ws = f.Server.CreateWebSocketClient();
        ws.ConfigureRequest = r => r.Headers.Authorization = $"Bearer {bidder.Token}";
        using var socket = await ws.ConnectAsync(new Uri($"ws://localhost/{prefix}/auctions/{id}"), CancellationToken.None);

        var placed = await bidder.DataAsync(bidder.BidAsync(id, 150));
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frame = await socket.ReceiveAsync(buffer, timeout.Token);
        using var msg = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
        Assert.Equal("bid.placed", msg.RootElement.GetProperty("event").GetString());
        var data = msg.RootElement.GetProperty("data");
        Assert.Equal(150m, data.GetProperty("currentPrice").GetDecimal());
        Assert.Equal(placed.GetProperty("latestBids")[0].GetProperty("id").GetInt64(), data.GetProperty("bid").GetProperty("id").GetInt64());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Theory]
    [InlineData("api/ws")]
    [InlineData("ws")]
    public async Task Websocket_requires_a_token(string prefix)
    {
        var id = await f.NewAuctionAsync();
        var ws = f.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync(new Uri($"ws://localhost/{prefix}/auctions/{id}"), CancellationToken.None));
    }

    // ---------- favourites, notifications ----------

    [Fact]
    public async Task Favorites_are_idempotent()
    {
        var c = await CustomerAsync();
        var id = await f.NewAuctionAsync();
        await c.DataAsync(c.PostAsync($"/api/v1/favorites/{id}"));
        await c.DataAsync(c.PostAsync($"/api/v1/favorites/{id}"));
        Assert.Contains((await c.DataAsync(c.GetAsync("/api/v1/favorites"))).EnumerateArray(), a => a.GetProperty("id").GetInt32() == id);
        await c.DataAsync(c.DeleteAsync($"/api/v1/favorites/{id}"));
        await c.DataAsync(c.DeleteAsync($"/api/v1/favorites/{id}"));
        Assert.DoesNotContain((await c.DataAsync(c.GetAsync("/api/v1/favorites"))).EnumerateArray(), a => a.GetProperty("id").GetInt32() == id);
        Assert.Equal("NOT_FOUND", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/favorites/999999999")));
    }

    [Fact]
    public async Task Notifications_mark_read_soft_delete_and_restore()
    {
        var (userId, email) = await f.NewCustomerAsync();
        var nid = await f.DbAsync(async db =>
        {
            var n = new Notification { UserId = userId, Kind = NotificationKinds.TopUpApproved, Amount = 25, CreatedAtUtc = DateTime.UtcNow };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            return n.Id;
        });
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var n = (await c.DataAsync(c.GetAsync("/api/v1/notifications")))[0];
        Assert.Equal("wallet", n.GetProperty("type").GetString());
        Assert.False(n.GetProperty("read").GetBoolean());

        await c.DataAsync(c.PostAsync("/api/v1/notifications/read", new { ids = new[] { nid } }));
        await c.DataAsync(c.DeleteAsync($"/api/v1/notifications/{nid}"));
        Assert.Equal(0, (await c.DataAsync(c.GetAsync("/api/v1/notifications"))).GetArrayLength());
        await c.DataAsync(c.PostAsync($"/api/v1/notifications/{nid}/restore"));
        var restored = (await c.DataAsync(c.GetAsync("/api/v1/notifications")))[0];
        Assert.True(restored.GetProperty("read").GetBoolean());
    }

    // ---------- wallet ----------

    [Fact]
    public async Task Wallet_requests_appear_as_pending_and_withdrawals_cannot_exceed_the_balance()
    {
        var c = await CustomerAsync(wallet: 100);
        await c.DataAsync(c.PostAsync("/api/v1/wallet/increase", new { amount = 250.0 }));
        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/wallet/withdraw", new { amount = 150 })));
        await c.DataAsync(c.PostAsync("/api/v1/wallet/withdraw", new { amount = 60 }));
        // 60 is already pending: only 40 is left to request.
        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/wallet/withdraw", new { amount = 50 })));

        var w = await c.DataAsync(c.GetAsync("/api/v1/wallet"));
        Assert.Equal(100m, w.GetProperty("wallet").GetDecimal()); // requests never move money by themselves
        var tx = w.GetProperty("transaction").EnumerateArray().ToList();
        Assert.Contains(tx, t => t.GetProperty("type").GetString() == "Deposit" && t.GetProperty("status").GetString() == "Pending" && t.GetProperty("amount").GetDecimal() == 250);
        Assert.Contains(tx, t => t.GetProperty("type").GetString() == "Withdraw" && t.GetProperty("status").GetString() == "Pending");
    }

    // ---------- profile ----------

    [Fact]
    public async Task Profile_update_image_and_password_change()
    {
        var c = await CustomerAsync();
        var phone = "+962" + NewPhone()[1..];
        var email = $"p{Guid.NewGuid():N}@test.local";
        var p = await c.DataAsync(c.PutAsync("/api/v1/profile", new { fullName = "Renamed", email, phone }));
        Assert.Equal("Renamed", p.GetProperty("fullName").GetString());
        Assert.Equal(phone, p.GetProperty("phone").GetString());

        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestFiles.Png);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", "me.png");
        var image = (await c.DataAsync(c.Http.PostAsync("/api/v1/profile/image", form))).GetProperty("image").GetString();
        Assert.Equal(image, (await c.DataAsync(c.GetAsync("/api/v1/profile"))).GetProperty("image").GetString());
        Assert.Equal(HttpStatusCode.OK, (await f.Client().GetAsync(new Uri(image!).PathAndQuery)).StatusCode); // loadable without a token

        var wrong = await c.PostAsync("/api/v1/profile/password", new { currentPassword = "nope", newPassword = "Another#2026" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ApiClient.ErrorCodeAsync(wrong));
        await c.DataAsync(c.PostAsync("/api/v1/profile/password", new { currentPassword = ApiFactory.Password, newPassword = "Another#2026" }));
        await c.DataAsync(c.GetAsync("/api/v1/profile")); // this device stays signed in
    }

    [Fact]
    public async Task Delete_account_signs_out_and_frees_the_email()
    {
        var (_, email) = await f.NewCustomerAsync(0);   // an account with money in it cannot be deleted (see WalletSafeguardsTests)
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        await c.DataAsync(c.DeleteAsync("/api/v1/profile"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().LoginAsync(email, ApiFactory.Password)).StatusCode);
        Assert.False(await f.DbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task My_auctions_lists_live_bids_and_won_auctions()
    {
        var (_, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var live = await f.NewAuctionAsync(start: 100, increment: 10);
        await c.DataAsync(c.BidAsync(live, 110));
        var mine = await c.DataAsync(c.GetAsync("/api/v1/profile/auctions"));
        Assert.Contains(mine.GetProperty("live").EnumerateArray(), a => a.GetProperty("id").GetInt32() == live);
        Assert.Equal(JsonValueKind.Array, mine.GetProperty("won").ValueKind);
    }
}

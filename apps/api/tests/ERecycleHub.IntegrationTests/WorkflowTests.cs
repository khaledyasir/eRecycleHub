using System.Net;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PublisherOwnershipTests(ApiFactory f)
{
    private static object Input(decimal start = 100, decimal increment = 10, DateTime? startsAt = null, DateTime? endsAt = null, string title = "Lot <script>alert(1)</script>") => new
    {
        countryId = 1, titleEn = title, titleAr = "دفعة", descEn = "<p>ok</p><script>alert(1)</script>", startPrice = start, bidIncrement = increment, insuranceAmount = 0,
        startsAtUtc = startsAt ?? DateTime.UtcNow.AddHours(-1), endsAtUtc = endsAt ?? DateTime.UtcNow.AddDays(3), isActive = true,
        specs = new[] { new { titleEn = "Weight", titleAr = "الوزن", valueEn = "3 t", valueAr = "3 طن" } },
    };

    private async Task<int> CreateAsync(ApiClient c, object? input = null)
    {
        var r = await c.MultipartAsync(HttpMethod.Post, "/api/seller/auctions", input ?? Input(), ("mainImage", "main.png", TestFiles.Png), ("images", "g1.png", TestFiles.Png));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Publisher_creates_auction_with_sanitised_text_specs_and_images()
    {
        var p = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        var id = await CreateAsync(p);
        var a = await f.DbAsync(db => db.Auctions.Include(x => x.Specs).Include(x => x.Images).AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(await f.PublisherIdAsync(DevSeed.PublisherEmail), a.PublisherId);
        Assert.DoesNotContain("<script", a.DescEn);
        Assert.Equal(100m, a.CurrentPrice);
        Assert.Single(a.Specs);
        Assert.Single(a.Images);
        Assert.Matches("^[0-9]{8}$", a.RefNumber);
    }

    [Fact]
    public async Task Another_publisher_cannot_read_edit_toggle_delete_or_change_images()
    {
        var owner = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        var id = await CreateAsync(owner);
        var imageId = await f.DbAsync(db => db.AuctionImages.Where(i => i.AuctionId == id).Select(i => i.Id).FirstAsync());
        var other = await f.SignedInAsync(DevSeed.Publisher2Email, DevSeed.PublisherPassword);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/seller/auctions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.MultipartAsync(HttpMethod.Put, $"/api/seller/auctions/{id}", Input(title: "hijacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/seller/auctions/{id}/active", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/seller/auctions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/seller/auctions/{id}/images/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.MultipartAsync(HttpMethod.Post, $"/api/seller/auctions/{id}/images", new { }, ("images", "x.png", TestFiles.Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/seller/auctions/{id}/bids")).StatusCode);

        var a = await f.DbAsync(db => db.Auctions.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.True(a.IsActive);
        Assert.StartsWith("Lot", a.TitleEn);
        Assert.Equal(1, await f.DbAsync(db => db.AuctionImages.CountAsync(i => i.AuctionId == id)));
    }

    [Fact]
    public async Task Price_terms_are_frozen_once_bids_exist_and_auctions_with_bids_cannot_be_deleted()
    {
        var owner = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        var starts = DateTime.UtcNow.AddHours(-1);
        var ends = DateTime.UtcNow.AddDays(3);
        var id = await CreateAsync(owner, Input(startsAt: starts, endsAt: ends));
        var bidder = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await bidder.BidAsync(id, 110)).StatusCode);

        var changePrice = await owner.MultipartAsync(HttpMethod.Put, $"/api/seller/auctions/{id}", Input(start: 1, startsAt: starts, endsAt: ends));
        Assert.Equal("auction.terms_frozen", await ApiClient.CodeAsync(changePrice));
        var extend = await owner.MultipartAsync(HttpMethod.Put, $"/api/seller/auctions/{id}", Input(startsAt: starts, endsAt: ends.AddDays(1), title: "Renamed lot"));
        Assert.Equal(HttpStatusCode.NoContent, extend.StatusCode);
        Assert.Equal(110m, await f.DbAsync(db => db.Auctions.Where(a => a.Id == id).Select(a => a.CurrentPrice).SingleAsync()));
        Assert.Equal("auction.has_bids", await ApiClient.CodeAsync(await owner.DeleteAsync($"/api/seller/auctions/{id}")));
    }

    [Fact]
    public async Task Bidders_list_only_shows_people_who_bid_on_my_auctions_and_hides_their_finances()
    {
        var owner = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        var id = await CreateAsync(owner);
        var (bidderId, bidderEmail) = await f.NewCustomerAsync();
        await (await f.SignedInAsync(bidderEmail, ApiFactory.Password)).BidAsync(id, 110);
        var (strangerId, _) = await f.NewCustomerAsync();

        var details = await owner.GetAsync($"/api/seller/bidders/{bidderId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var body = await details.Content.ReadAsStringAsync();
        Assert.DoesNotContain("wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cardNumber", body);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/seller/bidders/{strangerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await f.SignedInAsync(DevSeed.Publisher2Email, DevSeed.PublisherPassword)).GetAsync($"/api/seller/bidders/{bidderId}")).StatusCode);
    }

    [Fact]
    public async Task Publisher_cannot_delete_reviews_on_other_publishers_auctions()
    {
        var other = await f.SignedInAsync(DevSeed.Publisher2Email, DevSeed.PublisherPassword);
        var p1 = await f.PublisherIdAsync(DevSeed.PublisherEmail);
        var auction = await f.NewAuctionAsync(publisherId: p1);
        var reviewer = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        var r = await reviewer.PostAsync($"/api/auctions/{auction}/reviews", new { rating = 4, comment = "fine" });
        var reviewId = (await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/seller/reviews/{reviewId}")).StatusCode);
        Assert.Equal("review.already_reviewed", await ApiClient.CodeAsync(await reviewer.PostAsync($"/api/auctions/{auction}/reviews", new { rating = 5 })));
        Assert.Equal("review.invalid_rating", await ApiClient.CodeAsync(await reviewer.PostAsync($"/api/auctions/{await f.NewAuctionAsync()}/reviews", new { rating = 9 })));
    }

    [Fact]
    public async Task Deactivated_publishers_auctions_disappear_from_public_but_are_not_modified()
    {
        var (uid, email) = await f.NewCustomerAsync(wallet: 0, role: UserRole.Publisher);
        var pid = await f.DbAsync(async db =>
        {
            var p = new Publisher { UserId = uid, CountryId = 1, FirstName = "Temp", CreatedAtUtc = DateTime.UtcNow };
            db.Publishers.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
        var live = await f.NewAuctionAsync(publisherId: pid);
        var archived = await f.NewAuctionAsync(publisherId: pid, active: false);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/publishers/{pid}/active", new { active = false })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await f.Client().GetAsync($"/api/auctions/{live}")).StatusCode);
        Assert.True(await f.DbAsync(db => db.Auctions.Where(a => a.Id == live).Select(a => a.IsActive).SingleAsync()));
        Assert.False(await f.DbAsync(db => db.Auctions.Where(a => a.Id == archived).Select(a => a.IsActive).SingleAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().LoginAsync(email, ApiFactory.Password)).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public class AdminWorkflowTests(ApiFactory f)
{
    [Fact]
    public async Task Application_approval_creates_a_publisher_once_with_the_chosen_password()
    {
        var anon = f.Client();
        var email = $"app{Guid.NewGuid():N}@test.local";
        var submit = await anon.PostAsync("/api/applications", new { name = "Recycler", email, mobile = "07", company = "R Ltd", password = "ChosenPass#2026", countryId = 1, categoryIds = new[] { 1 } });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var appId = (await submit.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
        Assert.False(PasswordService.IsLegacyV1(await f.DbAsync(db => db.PublisherApplications.Where(a => a.Id == appId).Select(a => a.PasswordHash!).SingleAsync())));

        var admins = new[] { await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword), await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword) };
        foreach (var a in admins) await a.CsrfAsync();
        var results = await Task.WhenAll(admins.Select(a => a.PostAsync($"/api/admin/applications/{appId}/approve")));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);

        var user = await f.DbAsync(db => db.Users.Include(u => u.Publisher).SingleAsync(u => u.Email == email));
        Assert.Equal(UserRole.Publisher, user.Role);
        Assert.NotNull(user.Publisher);
        Assert.Null(await f.DbAsync(db => db.PublisherApplications.Where(a => a.Id == appId).Select(a => a.PasswordHash).SingleAsync()));
        Assert.Equal(HttpStatusCode.OK, (await f.Client().LoginAsync(email, "ChosenPass#2026")).StatusCode);
    }

    [Fact]
    public async Task Winner_is_emailed_only_once()
    {
        var auction = await f.NewAuctionAsync(startHours: -2, endHours: 1);
        var (winnerId, winnerEmail) = await f.NewCustomerAsync();
        await (await f.SignedInAsync(winnerEmail, ApiFactory.Password)).BidAsync(auction, 120);
        await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, DateTime.UtcNow.AddMinutes(-1))));

        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        var first = await admin.PostAsync($"/api/admin/auctions/{auction}/notify-winner");
        var second = await admin.PostAsync($"/api/admin/auctions/{auction}/notify-winner");
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("sent").GetInt32());
        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("sent").GetInt32());
        Assert.Equal(1, await f.DbAsync(db => db.Notifications.CountAsync(n => n.UserId == winnerId && n.Kind == NotificationKinds.AuctionWon)));
    }

    [Fact]
    public async Task Admin_cms_crud_round_trip_and_in_use_protection()
    {
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        var create = await admin.PostAsync("/api/admin/faq-categories", new { titleEn = "Payments", titleAr = "الدفع", sortOrder = 5, isActive = true });
        var catId = (await create.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
        var faq = await admin.PostAsync("/api/admin/faqs", new { categoryId = catId, questionEn = "Q?", questionAr = "س؟", answerEn = "<p>A</p><script>x</script>", answerAr = "<p>ج</p>", sortOrder = 1, isActive = true });
        Assert.Equal(HttpStatusCode.OK, faq.StatusCode);
        Assert.Equal("faqcategory.not_empty", await ApiClient.CodeAsync(await admin.DeleteAsync($"/api/admin/faq-categories/{catId}")));
        var saved = await f.DbAsync(db => db.Faqs.SingleAsync(x => x.CategoryId == catId));
        Assert.Equal("<p>ج</p>", saved.AnswerAr); // legacy never saved the Arabic answer
        Assert.DoesNotContain("script", saved.AnswerEn);
        Assert.Contains(await f.Client().GetJsonAsync<JsonElement[]>("/api/content/faq"), c => c.GetProperty("id").GetInt32() == catId);
        Assert.Equal("country.in_use", await ApiClient.CodeAsync(await admin.DeleteAsync("/api/admin/countries/1")));
        Assert.True(await f.DbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "faq.create")));
    }

    [Fact]
    public async Task Homepage_hero_is_a_single_record_served_on_home_only_while_active()
    {
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        await f.DbAsync(db => db.HomeHeroes.ExecuteDeleteAsync());
        Assert.Equal(JsonValueKind.Null, (await f.Client().GetJsonAsync<JsonElement>("/api/home")).GetProperty("hero").ValueKind);

        var input = new { titleEn = "Sell {scrap}\n[fast]", titleAr = "بع {الخردة}", leadEn = "Lead", primaryCtaUrl = "/auctions", secondaryCtaUrl = "https://example.com/x", showSearch = false, isActive = true };
        var r = await admin.MultipartAsync(HttpMethod.Post, "/api/admin/hero", input, ("image", "hero.png", TestFiles.Png));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
        Assert.Equal("cms.hero_exists", await ApiClient.CodeAsync(await admin.MultipartAsync(HttpMethod.Post, "/api/admin/hero", input)));
        Assert.Equal("cms.invalid_link", await ApiClient.CodeAsync(await admin.MultipartAsync(HttpMethod.Put, $"/api/admin/hero/{id}", input with { primaryCtaUrl = "javascript:alert(1)" })));
        Assert.Equal("cms.too_long", await ApiClient.CodeAsync(await admin.MultipartAsync(HttpMethod.Put, $"/api/admin/hero/{id}", input with { titleEn = new string('x', 301) })));

        var hero = (await f.Client().GetJsonAsync<JsonElement>("/api/home")).GetProperty("hero");
        Assert.Equal("Sell {scrap}\n[fast]", hero.GetProperty("titleEn").GetString());
        Assert.False(hero.GetProperty("showSearch").GetBoolean());
        var imageId = hero.GetProperty("imageFileId").GetString();
        Assert.Equal(HttpStatusCode.OK, (await f.Client().GetAsync($"/api/files/{imageId}")).StatusCode);

        // Saving without a new picture keeps the current one; deactivating hides it from the homepage but keeps it for the admin.
        Assert.Equal(HttpStatusCode.OK, (await admin.MultipartAsync(HttpMethod.Put, $"/api/admin/hero/{id}", input with { isActive = false })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await f.Client().GetJsonAsync<JsonElement>("/api/home")).GetProperty("hero").ValueKind);
        var rows = await admin.GetJsonAsync<JsonElement[]>("/api/admin/hero");
        Assert.Equal(imageId, Assert.Single(rows).GetProperty("imageFileId").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await (await f.SignedInAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword)).GetAsync("/api/admin/hero")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/hero/{id}")).StatusCode);
        Assert.True(await f.DbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "hero.delete")));
    }

    [Fact]
    public async Task Report_csv_neutralises_formula_injection()
    {
        var auction = await f.NewAuctionAsync(startHours: -3, endHours: -1);
        await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.TitleEn, "=HYPERLINK(\"http://evil\")")));
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        var csv = await (await admin.GetAsync("/api/admin/reports/auctions?format=csv")).Content.ReadAsStringAsync();
        Assert.Contains("\"'=HYPERLINK", csv);
    }
}

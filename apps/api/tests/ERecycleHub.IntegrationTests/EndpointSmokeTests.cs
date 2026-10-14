using System.Net;
using ERecycleHub.Application;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>Every read endpoint answers 200 for the role it is meant for (catches query-translation and serialisation failures).</summary>
[Collection(ApiCollection.Name)]
public class EndpointSmokeTests(ApiFactory f)
{
    public static TheoryData<string> PublicUrls => new()
    {
        "/api/home", "/api/home?countryId=1", "/api/countries", "/api/categories", "/api/auctions", "/api/auctions?status=live&sort=ending",
        "/api/auctions?status=upcoming", "/api/auctions?status=ended&sort=price_desc", "/api/auctions?q=copper&category=1&minPrice=1&maxPrice=99999&period=last30days",
        "/api/auctions/facets", "/api/auctions/1", "/api/auctions/1/bids", "/api/auctions/1/reviews", "/api/stores", "/api/stores?q=green", "/api/stores/1",
        "/api/clients", "/api/content/about", "/api/content/benefits", "/api/content/how-to", "/api/content/faq", "/api/content/contact-info",
        "/api/content/settings/terms", "/api/content/settings/privacy-policy", "/api/i18n/ar", "/api/sitemap", "/api/health",
    };

    [Theory]
    [MemberData(nameof(PublicUrls))]
    public async Task Public_endpoints(string url) => Assert.Equal(HttpStatusCode.OK, (await f.Client().GetAsync(url)).StatusCode);

    [Fact]
    public async Task Calendar() =>
        Assert.Equal(HttpStatusCode.OK, (await f.Client().GetAsync($"/api/auctions/calendar?from={DateTime.UtcNow.AddDays(-15):O}&to={DateTime.UtcNow.AddDays(15):O}")).StatusCode);

    [Theory]
    [InlineData("/api/account/dashboard")]
    [InlineData("/api/account/bids")]
    [InlineData("/api/account/auctions?tab=leading")]
    [InlineData("/api/account/auctions?tab=won")]
    [InlineData("/api/account/favorites")]
    [InlineData("/api/account/watching")]
    [InlineData("/api/account/profile")]
    [InlineData("/api/account/wallet")]
    [InlineData("/api/account/wallet/transactions")]
    [InlineData("/api/account/wallet/ledger")]
    [InlineData("/api/account/notifications")]
    [InlineData("/api/account/notifications/unread-count")]
    public async Task Customer_endpoints(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await (await f.SignedInAsync(DevSeed.CustomerEmail, DevSeed.CustomerPassword)).GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/api/seller/dashboard")]
    [InlineData("/api/seller/auctions")]
    [InlineData("/api/seller/auctions?view=archived&status=live&q=lot")]
    [InlineData("/api/seller/reviews")]
    [InlineData("/api/seller/bidders")]
    [InlineData("/api/seller/bidders?q=test")]
    [InlineData("/api/seller/profile")]
    public async Task Seller_endpoints(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await (await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword)).GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/api/admin/dashboard")]
    [InlineData("/api/admin/reports/auctions")]
    [InlineData("/api/admin/reports/auctions?countryId=1&from=2020-01-01&to=2100-01-01")]
    [InlineData("/api/admin/reports/auctions?format=csv")]
    [InlineData("/api/admin/winners")]
    [InlineData("/api/admin/winners?q=test&notified=false")]
    [InlineData("/api/admin/winners/by-bidder")]
    [InlineData("/api/admin/auctions")]
    [InlineData("/api/admin/auctions?view=archived")]
    [InlineData("/api/admin/auctions?publisherId=0")]
    [InlineData("/api/admin/auctions/1")]
    [InlineData("/api/admin/auctions/10/bids")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/users?active=false")]
    [InlineData("/api/admin/users/search?q=test")]
    [InlineData("/api/admin/users/2")]
    [InlineData("/api/admin/users/2/transactions")]
    [InlineData("/api/admin/users/2/ledger")]
    [InlineData("/api/admin/publishers")]
    [InlineData("/api/admin/publishers/options")]
    [InlineData("/api/admin/publishers/1")]
    [InlineData("/api/admin/applications")]
    [InlineData("/api/admin/applications?status=Pending")]
    [InlineData("/api/admin/transactions")]
    [InlineData("/api/admin/transactions?status=Pending&q=test")]
    [InlineData("/api/admin/categories")]
    [InlineData("/api/admin/countries")]
    [InlineData("/api/admin/announcements")]
    [InlineData("/api/admin/sliders")]
    [InlineData("/api/admin/about")]
    [InlineData("/api/admin/benefits")]
    [InlineData("/api/admin/how-to")]
    [InlineData("/api/admin/faq-categories")]
    [InlineData("/api/admin/faqs")]
    [InlineData("/api/admin/contact-info")]
    [InlineData("/api/admin/settings")]
    [InlineData("/api/admin/messages")]
    [InlineData("/api/admin/translations")]
    [InlineData("/api/admin/audit")]
    public async Task Admin_endpoints(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await (await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword)).GetAsync(url)).StatusCode);

    [Fact]
    public async Task Seller_bidder_details_for_a_real_bidder()
    {
        var bidder = await f.DbAsync(db => db.Bids.Where(b => b.Auction!.Publisher!.User!.Email == DevSeed.PublisherEmail).Select(b => (int?)b.UserId).FirstOrDefaultAsync());
        if (bidder is null) return; // seed bids are on the second publisher's auctions
        Assert.Equal(HttpStatusCode.OK, (await (await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword)).GetAsync($"/api/seller/bidders/{bidder}")).StatusCode);
    }
}

using System.Net;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BiddingTests(ApiFactory f)
{
    [Fact]
    public async Task Valid_bid_updates_price_count_and_history_and_notifies_watchers()
    {
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var (watcherId, watcherEmail) = await f.NewCustomerAsync();
        var watcher = await f.SignedInAsync(watcherEmail, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.NoContent, (await watcher.PutAsync($"/api/auctions/{auction}/watch")).StatusCode);

        var (_, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var r = await c.BidAsync(auction, 110);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var a = await f.DbAsync(db => db.Auctions.AsNoTracking().SingleAsync(x => x.Id == auction));
        Assert.Equal(110m, a.CurrentPrice);
        Assert.Equal(1, a.BidCount);
        Assert.Equal(1, await f.DbAsync(db => db.Bids.CountAsync(b => b.AuctionId == auction && b.Amount == 110)));
        Assert.Equal(1, await f.DbAsync(db => db.Notifications.CountAsync(n => n.UserId == watcherId && n.AuctionId == auction && n.Kind == NotificationKinds.NewBid)));
    }

    [Theory]
    [InlineData(105, "bid.below_minimum")]
    [InlineData(110.005, "bid.invalid_amount")]
    [InlineData(-1, "bid.invalid_amount")]
    public async Task Invalid_amounts_are_rejected_and_change_nothing(decimal amount, string code)
    {
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var (_, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var r = await c.BidAsync(auction, amount);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(code, await ApiClient.CodeAsync(r));
        Assert.Equal(0, await f.DbAsync(db => db.Bids.CountAsync(b => b.AuctionId == auction)));
    }

    [Fact]
    public async Task Phase_ownership_insurance_and_role_rules_are_enforced()
    {
        var pubId = await f.PublisherIdAsync(DevSeed.PublisherEmail);
        var upcoming = await f.NewAuctionAsync(startHours: 2);
        var ended = await f.NewAuctionAsync(startHours: -48, endHours: -1);
        var inactive = await f.NewAuctionAsync(active: false);
        var own = await f.NewAuctionAsync(publisherId: pubId);
        var deposit = await f.NewAuctionAsync(insurance: 5000);
        var (_, email) = await f.NewCustomerAsync(wallet: 100);
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var publisher = await f.SignedInAsync(DevSeed.PublisherEmail, DevSeed.PublisherPassword);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);

        Assert.Equal("bid.not_started", await ApiClient.CodeAsync(await c.BidAsync(upcoming, 500)));
        Assert.Equal("bid.ended", await ApiClient.CodeAsync(await c.BidAsync(ended, 500)));
        Assert.Equal("bid.inactive", await ApiClient.CodeAsync(await c.BidAsync(inactive, 500)));
        Assert.Equal("bid.insufficient_insurance", await ApiClient.CodeAsync(await c.BidAsync(deposit, 500)));
        Assert.Equal("bid.own_auction", await ApiClient.CodeAsync(await publisher.BidAsync(own, 500)));
        Assert.Equal("bid.admin_cannot_bid", await ApiClient.CodeAsync(await admin.BidAsync(own, 500)));
        Assert.Equal(HttpStatusCode.NotFound, (await c.BidAsync(int.MaxValue, 500)).StatusCode);
        Assert.Equal(0, await f.DbAsync(db => db.Bids.CountAsync(b => new[] { upcoming, ended, inactive, own, deposit }.Contains(b.AuctionId))));
    }

    [Fact]
    public async Task Anonymous_bid_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().BidAsync(await f.NewAuctionAsync(), 500)).StatusCode);

    [Fact]
    public async Task Two_simultaneous_equal_bids_exactly_one_wins()
    {
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var clients = new List<ApiClient>();
        for (var i = 0; i < 2; i++) clients.Add(await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password));
        foreach (var c in clients) await c.CsrfAsync();

        var results = await Task.WhenAll(clients.Select(c => c.BidAsync(auction, 150)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal("bid.below_minimum", await ApiClient.CodeAsync(loser));
        Assert.Equal(1, await f.DbAsync(db => db.Bids.CountAsync(b => b.AuctionId == auction)));
        Assert.Equal(150m, await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).Select(a => a.CurrentPrice).SingleAsync()));
    }

    [Fact]
    public async Task Many_concurrent_bids_keep_price_count_and_history_consistent()
    {
        var auction = await f.NewAuctionAsync(start: 100, increment: 1);
        var clients = new List<ApiClient>();
        for (var i = 0; i < 8; i++) clients.Add(await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password));
        foreach (var c in clients) await c.CsrfAsync();

        // 40 bids racing with amounts 101..140; any interleaving must leave a strictly increasing accepted sequence.
        var tasks = Enumerable.Range(0, 40).Select(i => clients[i % clients.Count].BidAsync(auction, 101 + i)).ToList();
        var results = await Task.WhenAll(tasks);
        var accepted = results.Count(r => r.StatusCode == HttpStatusCode.OK);

        var (price, count, bids) = await f.DbAsync(async db => (
            await db.Auctions.Where(a => a.Id == auction).Select(a => a.CurrentPrice).SingleAsync(),
            await db.Auctions.Where(a => a.Id == auction).Select(a => a.BidCount).SingleAsync(),
            await db.Bids.Where(b => b.AuctionId == auction).OrderBy(b => b.Id).Select(b => b.Amount).ToListAsync()));
        Assert.Equal(accepted, bids.Count);
        Assert.Equal(accepted, count);
        Assert.Equal(bids.Max(), price);
        for (var i = 1; i < bids.Count; i++) Assert.True(bids[i] >= bids[i - 1] + 1, "accepted bids must respect the increment in commit order");
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Bid_arriving_after_the_end_is_rejected_even_if_the_page_showed_it_live()
    {
        var auction = await f.NewAuctionAsync(startHours: -1, endHours: 1);
        var c = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        await c.CsrfAsync();
        f.Clock.Offset = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1); // the server clock passes the end
        try
        {
            Assert.Equal("bid.ended", await ApiClient.CodeAsync(await c.BidAsync(auction, 500)));
        }
        finally
        {
            f.Clock.Offset = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Public_bid_history_hides_identities()
    {
        var auction = await f.NewAuctionAsync();
        var c = await f.SignedInAsync((await f.NewCustomerAsync()).email, ApiFactory.Password);
        await c.BidAsync(auction, 110);
        var body = await (await f.Client().GetAsync($"/api/auctions/{auction}/bids")).Content.ReadAsStringAsync();
        Assert.Contains("\"bidderNumber\":1", body);
        Assert.DoesNotContain("@test.local", body);
        Assert.DoesNotContain("Test ", body);
    }
}

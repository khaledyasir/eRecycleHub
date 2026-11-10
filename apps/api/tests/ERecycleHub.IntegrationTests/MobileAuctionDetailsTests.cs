using System.Text.Json;
using ERecycleHub.Application;

namespace ERecycleHub.IntegrationTests;

/// <summary>The viewer fields on the mobile auction details and on the bid response: isLeading, heldByViewer, debitsWallet, availableAfter, releasedFromPrevious.</summary>
[Collection(ApiCollection.Name)]
public class MobileAuctionDetailsTests(ApiFactory f)
{
    private sealed class FlagScope : IDisposable
    {
        private readonly TestBiddingSettings _flags;
        public FlagScope(TestBiddingSettings flags, bool on) { _flags = flags; flags.On = on; }
        public void Dispose() => _flags.On = false;
    }

    private async Task<ApiClient> BidderAsync(decimal wallet = 500) => await f.SignedInAsync((await f.NewCustomerAsync(wallet)).email, ApiFactory.Password);

    private static Task<JsonElement> DetailsAsync(ApiClient c, int auction) => c.DataAsync(c.GetAsync($"/api/v1/auctions/{auction}"));

    [Fact]
    public async Task With_wallet_bidding_on_the_bid_response_and_the_details_say_who_leads_and_what_is_held()
    {
        using var flag = new FlagScope(f.Flags, true);
        var a = await BidderAsync();
        var b = await BidderAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var first = await a.DataAsync(a.BidAsync(auction, 110));
        Assert.True(first.GetProperty("isLeading").GetBoolean());
        Assert.Equal(110m, first.GetProperty("heldByViewer").GetDecimal());
        Assert.True(first.GetProperty("debitsWallet").GetBoolean());
        Assert.Equal(390m, first.GetProperty("availableAfter").GetDecimal());
        Assert.Equal(0m, first.GetProperty("releasedFromPrevious").GetDecimal());      // nobody to release on the first bid
        Assert.Equal(390m, first.GetProperty("wallet").GetDecimal());

        var second = await b.DataAsync(b.BidAsync(auction, 120));
        Assert.True(second.GetProperty("isLeading").GetBoolean());
        Assert.Equal(120m, second.GetProperty("heldByViewer").GetDecimal());
        Assert.Equal(380m, second.GetProperty("availableAfter").GetDecimal());
        Assert.Equal(110m, second.GetProperty("releasedFromPrevious").GetDecimal());   // A's hold was released

        var asA = await DetailsAsync(a, auction);                                      // A was outbid
        Assert.False(asA.GetProperty("isLeading").GetBoolean());
        Assert.Equal(0m, asA.GetProperty("heldByViewer").GetDecimal());
        Assert.Equal(500m, asA.GetProperty("wallet").GetDecimal());
        var asB = await DetailsAsync(b, auction);
        Assert.True(asB.GetProperty("isLeading").GetBoolean());
        Assert.Equal(120m, asB.GetProperty("heldByViewer").GetDecimal());
        Assert.True(asB.GetProperty("debitsWallet").GetBoolean());
    }

    [Fact]
    public async Task The_bid_only_fields_are_absent_from_the_plain_details()
    {
        using var flag = new FlagScope(f.Flags, true);
        var a = await BidderAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await a.DataAsync(a.BidAsync(auction, 110));

        var details = await DetailsAsync(a, auction);

        Assert.False(details.TryGetProperty("availableAfter", out _));
        Assert.False(details.TryGetProperty("releasedFromPrevious", out _));
        // The fields the app already reads are still there, with their original names.
        foreach (var name in new[] { "id", "wallet", "insuranceAmount", "latestBids", "termAndConditon", "isexpire", "currentPrice", "increaseRange" })
            Assert.True(details.TryGetProperty(name, out _), name);
    }

    [Fact]
    public async Task With_wallet_bidding_off_nothing_is_held_and_debitsWallet_is_false()
    {
        using var flag = new FlagScope(f.Flags, false);
        var a = await BidderAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var bid = await a.DataAsync(a.BidAsync(auction, 110));

        Assert.True(bid.GetProperty("isLeading").GetBoolean());
        Assert.Equal(0m, bid.GetProperty("heldByViewer").GetDecimal());
        Assert.False(bid.GetProperty("debitsWallet").GetBoolean());
        Assert.Equal(JsonValueKind.Number, bid.GetProperty("availableAfter").ValueKind);
        Assert.Equal(JsonValueKind.Number, bid.GetProperty("releasedFromPrevious").ValueKind);
    }

    [Fact]
    public async Task A_viewer_who_never_bid_is_not_leading_and_holds_nothing()
    {
        using var flag = new FlagScope(f.Flags, true);
        var a = await BidderAsync();
        var watcher = await BidderAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await a.DataAsync(a.BidAsync(auction, 110));

        var details = await DetailsAsync(watcher, auction);

        Assert.False(details.GetProperty("isLeading").GetBoolean());
        Assert.Equal(0m, details.GetProperty("heldByViewer").GetDecimal());
        Assert.True(details.GetProperty("debitsWallet").GetBoolean());
    }
}

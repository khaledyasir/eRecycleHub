using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    // ---------- isWinner and saleStatus ----------

    private sealed class TimeTravel : IDisposable
    {
        private readonly TestClock _clock;
        public TimeTravel(TestClock clock, TimeSpan by) { _clock = clock; clock.Offset = by; }
        public void Dispose() => _clock.Offset = TimeSpan.Zero;
    }

    private Task<int> LiveAuctionAsync() => f.NewAuctionAsync(startHours: -1, endHours: 2, start: 100, increment: 10);   // ends in 2 hours
    private TimeTravel AfterTheEnd(double hours = 3) => new(f.Clock, TimeSpan.FromHours(hours));

    private async Task CloseAsync(int auction)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuctionClosing>().CloseAsync(auction);
    }

    private static async Task<(bool winner, string? sale)> SaleAsync(ApiClient c, int auction)
    {
        var d = await DetailsAsync(c, auction);
        return (d.GetProperty("isWinner").GetBoolean(), d.GetProperty("saleStatus").ValueKind == JsonValueKind.Null ? null : d.GetProperty("saleStatus").GetString());
    }

    [Fact]
    public async Task A_running_auction_has_no_winner_and_no_sale_status()
    {
        using var flag = new FlagScope(f.Flags, true);
        var a = await BidderAsync();
        var auction = await LiveAuctionAsync();
        await a.DataAsync(a.BidAsync(auction, 110));

        Assert.Equal((false, (string?)null), await SaleAsync(a, auction));
    }

    [Fact]
    public async Task An_ended_auction_is_pending_until_it_is_closed_and_then_paid()
    {
        using var flag = new FlagScope(f.Flags, true);
        var winner = await BidderAsync();
        var other = await BidderAsync();
        var auction = await LiveAuctionAsync();
        await winner.DataAsync(winner.BidAsync(auction, 110));

        using (AfterTheEnd())
        {
            Assert.Equal((true, "Pending"), await SaleAsync(winner, auction));      // the winning bid holds money, the job has not run yet
            Assert.Equal((false, "Pending"), await SaleAsync(other, auction));
            await CloseAsync(auction);
            Assert.Equal((true, "Paid"), await SaleAsync(winner, auction));
            Assert.Equal((false, "Paid"), await SaleAsync(other, auction));
            var paid = await DetailsAsync(winner, auction);
            Assert.Equal(0m, paid.GetProperty("heldByViewer").GetDecimal());        // the hold became the payment
        }
    }

    [Fact]
    public async Task A_winner_who_cannot_pay_sees_PaymentFailed_until_they_top_up()
    {
        using var flag = new FlagScope(f.Flags, true);
        var (id, email) = await f.NewCustomerAsync(500);
        var winner = await f.SignedInAsync(email, ApiFactory.Password);
        var outbid = await BidderAsync();
        var stranger = await BidderAsync();
        var auction = await LiveAuctionAsync();
        await outbid.DataAsync(outbid.BidAsync(auction, 110));
        await winner.DataAsync(winner.BidAsync(auction, 120));
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 50m)));

        using (AfterTheEnd())
        {
            await CloseAsync(auction);
            Assert.Equal((true, "PaymentFailed"), await SaleAsync(winner, auction));
            Assert.Equal((false, "Pending"), await SaleAsync(outbid, auction));      // others are not told who failed to pay
            Assert.Equal((false, "Pending"), await SaleAsync(stranger, auction));
            await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 500m)));
            await CloseAsync(auction);                                               // the job's next tick
            Assert.Equal((true, "Paid"), await SaleAsync(winner, auction));
            Assert.Equal((false, "Paid"), await SaleAsync(outbid, auction));         // Paid is visible to everyone
            Assert.Equal((false, "Paid"), await SaleAsync(stranger, auction));
        }
    }

    [Fact]
    public async Task A_voided_sale_has_no_winner()
    {
        using var flag = new FlagScope(f.Flags, true);
        var (id, email) = await f.NewCustomerAsync(500);
        var winner = await f.SignedInAsync(email, ApiFactory.Password);
        var stranger = await BidderAsync();
        var auction = await LiveAuctionAsync();
        await winner.DataAsync(winner.BidAsync(auction, 110));
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 50m)));
        var admin = new Actor(await f.DbAsync(db => db.Users.Where(u => u.Email == DevSeed.AdminEmail).Select(u => u.Id).SingleAsync()), UserRole.Admin);

        using (AfterTheEnd())
        {
            await CloseAsync(auction);
            using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AuctionClosing>().VoidSaleAsync(admin, auction);

            Assert.Equal((false, "Voided"), await SaleAsync(winner, auction));       // the holder of the winning bid still sees what happened to the sale
            Assert.Equal((false, "Pending"), await SaleAsync(stranger, auction));    // everyone else sees Pending
        }
    }

    [Fact]
    public async Task A_sale_that_held_nothing_is_NotCharged_once_processed_or_once_it_is_older_than_the_job_looks_back()
    {
        using var flag = new FlagScope(f.Flags, false);                             // wallet bidding off: the bid holds nothing
        var a = await BidderAsync();
        var stranger = await BidderAsync();
        var processed = await LiveAuctionAsync();
        var old = await LiveAuctionAsync();
        await a.DataAsync(a.BidAsync(processed, 110));
        await a.DataAsync(a.BidAsync(old, 110));

        using (AfterTheEnd(3))
        {
            Assert.Equal((true, "Pending"), await SaleAsync(a, old));                // ended an hour ago: the job may still announce it
            await CloseAsync(processed);
            Assert.Equal((true, "NotCharged"), await SaleAsync(a, processed));       // closed and announced, nothing charged
            Assert.Equal((false, "Pending"), await SaleAsync(stranger, processed));  // others do not see that nothing was charged
        }
        using (AfterTheEnd(9))
        {
            Assert.Equal((true, "NotCharged"), await SaleAsync(a, old));             // ended 7 hours ago, beyond the 6-hour lookback, holds nothing
            Assert.Equal((false, "Pending"), await SaleAsync(stranger, old));
        }
    }
}

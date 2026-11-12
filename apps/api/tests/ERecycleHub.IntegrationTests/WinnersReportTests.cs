using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>Admin "Auction winners" page: an ended auction whose BidCount says it has bids but which has no Bid rows must not break the list (it used to throw, so the page returned 500 on every load).</summary>
[Collection(ApiCollection.Name)]
public class WinnersReportTests(ApiFactory f)
{
    [Fact]
    public async Task An_ended_auction_with_a_bid_count_but_no_bid_rows_is_skipped_instead_of_failing_the_list()
    {
        var phantom = await f.NewAuctionAsync(startHours: -5, endHours: -1);
        await f.DbAsync(db => db.Auctions.Where(a => a.Id == phantom).ExecuteUpdateAsync(s => s.SetProperty(a => a.BidCount, 3)));

        var (bidder, _) = await f.NewCustomerAsync();
        var real = await f.NewAuctionAsync(startHours: -5, endHours: -1);
        await f.DbAsync(async db =>
        {
            db.Bids.Add(new Bid { AuctionId = real, UserId = bidder, Amount = 120, CreatedAtUtc = DateTime.UtcNow.AddHours(-2) });
            await db.Auctions.Where(a => a.Id == real).ExecuteUpdateAsync(s => s.SetProperty(a => a.BidCount, 1).SetProperty(a => a.CurrentPrice, 120m));
            await db.SaveChangesAsync();
        });

        using var scope = f.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ReportsService>();

        var page = await reports.WinnersAsync(null, null, null, null, new PageQuery(1, 100));
        Assert.DoesNotContain(page.Items, r => r.AuctionId == phantom);
        var row = Assert.Single(page.Items, r => r.AuctionId == real);
        Assert.Equal((bidder, 120m), (row.UserId, row.Amount));

        var groups = await reports.WinnersByBidderAsync(null, null);
        Assert.Contains(groups, g => g.UserId == bidder);
    }
}

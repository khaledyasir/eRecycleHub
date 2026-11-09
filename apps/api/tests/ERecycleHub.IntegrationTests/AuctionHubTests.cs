using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ERecycleHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuctionHubTests(ApiFactory f)
{
    [Fact]
    public async Task Bid_is_pushed_to_watchers_of_that_auction_only()
    {
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var other = await f.NewAuctionAsync(start: 100, increment: 10);

        await using var conn = new HubConnectionBuilder().WithUrl(new Uri(f.Server.BaseAddress, "/hubs/auctions"), o =>
        {
            o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling; // the in-memory test server has no real sockets
        }).Build();
        var got = new TaskCompletionSource<int>();
        var count = 0;
        conn.On<System.Text.Json.JsonElement>("bid.placed", e => { count++; got.TrySetResult(e.GetProperty("bidsCount").GetInt32()); });
        await conn.StartAsync();
        await conn.InvokeAsync("Watch", auction);

        var (_, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        await c.BidAsync(other, 110);   // different room: must not arrive
        await c.BidAsync(auction, 110);

        Assert.Equal(1, await got.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Task.Delay(300);
        Assert.Equal(1, count);
    }
}

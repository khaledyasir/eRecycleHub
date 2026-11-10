using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>Phase 4: in-app notification rows and the pushes that announce them: one row per user and event, one push per row, the right devices, the right language.</summary>
[Collection(ApiCollection.Name)]
public class NotificationTests(ApiFactory f)
{
    private sealed class FlagScope : IDisposable
    {
        private readonly TestBiddingSettings _flags;
        public FlagScope(TestBiddingSettings flags, bool on) { _flags = flags; flags.On = on; }
        public void Dispose() => _flags.On = false;
    }

    private static string NewToken() => "tok-" + Guid.NewGuid().ToString("N");

    private async Task<(int id, ApiClient client, string token)> UserWithDeviceAsync(string? lang = null, decimal wallet = 1000)
    {
        var (id, email) = await f.NewCustomerAsync(wallet);
        var client = await f.SignedInAsync(email, ApiFactory.Password);
        if (lang is not null) client.Http.DefaultRequestHeaders.Add("lang", lang);
        var token = NewToken();
        await client.DataAsync(client.PostAsync("/api/v1/devices/fcm-token", new { token }));
        await f.IdleAsync();
        return (id, client, token);
    }

    private Task<List<Notification>> RowsAsync(string eventKey) => f.DbAsync(db => db.Notifications.Where(n => n.EventKey == eventKey).ToListAsync());
    private async Task<long> BidIdAsync(int auctionId, int userId, decimal amount) =>
        await f.DbAsync(db => db.Bids.Where(b => b.AuctionId == auctionId && b.UserId == userId && b.Amount == amount).Select(b => b.Id).SingleAsync());

    [Fact]
    public async Task A_bid_notifies_the_previous_leader_participants_and_watchers_but_not_the_bidder()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (c, clientC, tokenC) = await UserWithDeviceAsync();                 // took part earlier
        var (a, clientA, tokenA) = await UserWithDeviceAsync(lang: "en");       // the previous leader, English device
        var (w, _, tokenW) = await UserWithDeviceAsync();                       // only watching
        var (b, clientB, tokenB) = await UserWithDeviceAsync();                 // places the bid
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await f.DbAsync(async db => { db.AuctionWatches.Add(new AuctionWatch { UserId = w, AuctionId = auction, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); });
        await clientC.DataAsync(clientC.BidAsync(auction, 110));
        await clientA.DataAsync(clientA.BidAsync(auction, 120));
        await f.IdleAsync();
        var before = f.Push.Sent.Count;

        await clientB.DataAsync(clientB.BidAsync(auction, 130));
        await f.IdleAsync();

        var bidId = await BidIdAsync(auction, b, 130);
        var rows = await RowsAsync($"bid:{bidId}");
        Assert.Equal(NotificationKinds.Outbid, rows.Single(n => n.UserId == a).Kind);
        Assert.Equal(NotificationKinds.NewBid, rows.Single(n => n.UserId == c).Kind);
        Assert.Equal(NotificationKinds.NewBid, rows.Single(n => n.UserId == w).Kind);
        Assert.DoesNotContain(rows, n => n.UserId == b);                         // the bidder is not told about their own bid

        var sent = f.Push.Sent.Skip(before).ToList();
        PushMessage To(string token) => sent.Single(s => s.Token == token && s.Message.Type != "joined").Message;
        Assert.Equal("outbid", To(tokenA).Type);
        Assert.Equal("new_bid", To(tokenC).Type);
        Assert.Equal("new_bid", To(tokenW).Type);
        Assert.Equal("joined", Assert.Single(sent, s => s.Token == tokenB).Message.Type);   // B only hears that they joined
        var outbid = To(tokenA);
        Assert.Equal(rows.Single(n => n.UserId == a).Id.ToString(), outbid.Data["notification_id"]);
        Assert.Equal(auction.ToString(), outbid.Data["auction_id"]);
        Assert.Equal("130.00", outbid.Data["amount"]);
        Assert.All(outbid.Data.Values, v => Assert.False(string.IsNullOrEmpty(v)));
        Assert.Equal(outbid.Data["title_en"], outbid.Title);                    // A's device is English
        Assert.Equal(To(tokenC).Data["title_ar"], To(tokenC).Title);            // C's has no language: Arabic
    }

    [Fact]
    public async Task Joining_is_announced_once_and_a_repeated_job_sends_and_writes_nothing_more()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (b, clientB, tokenB) = await UserWithDeviceAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        await clientB.DataAsync(clientB.BidAsync(auction, 110));
        await clientB.DataAsync(clientB.BidAsync(auction, 120));   // a raise does not "join" again
        await f.IdleAsync();
        Assert.Single(f.Push.Sent, s => s.Token == tokenB && s.Message.Type == "joined" && s.Message.Data["auction_id"] == auction.ToString());
        Assert.Single(await f.DbAsync(db => db.Notifications.Where(n => n.UserId == b && n.Kind == NotificationKinds.Joined && n.AuctionId == auction).ToListAsync()));

        var sentBefore = f.Push.Sent.Count;
        var rowsBefore = await f.DbAsync(db => db.Notifications.CountAsync(n => n.AuctionId == auction));
        var bidId = await BidIdAsync(auction, b, 110);
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuctionNotifier>().NotifyBidPlacedAsync(bidId, joined: true, previousLeader: null);   // the same job again

        Assert.Equal(sentBefore, f.Push.Sent.Count);
        Assert.Equal(rowsBefore, await f.DbAsync(db => db.Notifications.CountAsync(n => n.AuctionId == auction)));
    }

    [Fact]
    public async Task Dead_tokens_reported_when_sending_are_deleted_and_the_row_stays()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (a, clientA, tokenA) = await UserWithDeviceAsync();
        var (_, clientB, _) = await UserWithDeviceAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await clientA.DataAsync(clientA.BidAsync(auction, 110));
        await f.IdleAsync();
        f.Push.DeadTokens.Add(tokenA);

        await clientB.DataAsync(clientB.BidAsync(auction, 120));
        await f.IdleAsync();

        Assert.False(await f.DbAsync(db => db.DeviceTokens.AnyAsync(d => d.Token == tokenA)));
        Assert.Single(await f.DbAsync(db => db.Notifications.Where(n => n.UserId == a && n.Kind == NotificationKinds.Outbid && n.AuctionId == auction).ToListAsync()));
    }

    [Fact]
    public async Task The_list_says_what_happened_and_which_auction_to_open_and_read_all_clears_the_unread_count()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (a, clientA, _) = await UserWithDeviceAsync();
        var (_, clientB, _) = await UserWithDeviceAsync();
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await clientA.DataAsync(clientA.BidAsync(auction, 110));
        await clientB.DataAsync(clientB.BidAsync(auction, 120));
        await f.IdleAsync();

        var list = await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications"));
        var outbid = list.EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "auction_outbid");
        Assert.Equal(auction, outbid.GetProperty("auctionId").GetInt32());
        Assert.Equal("bid", outbid.GetProperty("type").GetString());
        Assert.False(outbid.GetProperty("read").GetBoolean());
        Assert.Contains(list.EnumerateArray(), n => n.GetProperty("kind").GetString() == "auction_joined");

        var unread = (await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications/unread-count"))).GetProperty("count").GetInt32();
        Assert.True(unread >= 2);
        Assert.All((await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications?unread=true"))).EnumerateArray(), n => Assert.False(n.GetProperty("read").GetBoolean()));
        await clientA.DataAsync(clientA.PostAsync("/api/v1/notifications/read-all"));
        Assert.Equal(0, (await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications/unread-count"))).GetProperty("count").GetInt32());
        Assert.Empty((await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications?unread=true"))).EnumerateArray());
        Assert.Equal(1, (await clientA.DataAsync(clientA.GetAsync("/api/v1/notifications?page=1&pageSize=1"))).GetArrayLength());
        Assert.NotEqual(0, a);
    }
}

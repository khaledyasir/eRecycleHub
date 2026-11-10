using System.Net;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>Phase 2: auction topics, participants and push-token handling (against the fake push gateway).</summary>
[Collection(ApiCollection.Name)]
public class AuctionTopicTests(ApiFactory f)
{
    private static string NewToken() => "tok-" + Guid.NewGuid().ToString("N");

    private async Task<(int id, ApiClient client)> SignedInCustomerAsync()
    {
        var (id, email) = await f.NewCustomerAsync(500);
        return (id, await f.SignedInAsync(email, ApiFactory.Password));
    }

    private Task JoinAsync(int userId, int auctionId) => f.DbAsync(async db =>
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await new AuctionTopicService(db, f.Push, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuctionTopicService>.Instance).JoinAsync(userId, auctionId);
        await tx.CommitAsync();
    });

    private async Task<HttpResponseMessage> RegisterAsync(ApiClient c, string token, string? platform = null, string? deviceId = null)
    {
        var r = await c.PostAsync("/api/v1/devices/fcm-token", new { token, platform, deviceId });
        await f.IdleAsync();
        return r;
    }

    [Fact]
    public async Task A_new_auction_gets_its_topic_name_from_its_id()
    {
        var id = await f.NewAuctionAsync();
        var topic = await f.DbAsync(db => db.Auctions.Where(a => a.Id == id).Select(a => a.TopicName).SingleAsync());
        Assert.Equal($"auction_{id}", topic);
        Assert.Equal(topic, AuctionTopicService.TopicName(id));
    }

    [Fact]
    public async Task Joining_twice_keeps_one_row_and_reports_the_first_join_only()
    {
        var (userId, _) = await f.NewCustomerAsync();
        var auction = await f.NewAuctionAsync();
        var first = await f.DbAsync(async db => { await using var tx = await db.Database.BeginTransactionAsync(); var r = await Topics(db).JoinAsync(userId, auction); await tx.CommitAsync(); return r; });
        var second = await f.DbAsync(async db => { await using var tx = await db.Database.BeginTransactionAsync(); var r = await Topics(db).JoinAsync(userId, auction); await tx.CommitAsync(); return r; });
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await f.DbAsync(db => db.AuctionParticipants.CountAsync(p => p.AuctionId == auction && p.UserId == userId)));
    }

    [Fact]
    public async Task Concurrent_joins_create_exactly_one_participant()
    {
        var (userId, _) = await f.NewCustomerAsync();
        var auction = await f.NewAuctionAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.DbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var joined = await Topics(db).JoinAsync(userId, auction);
            await tx.CommitAsync();
            return joined;
        })));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, await f.DbAsync(db => db.AuctionParticipants.CountAsync(p => p.AuctionId == auction && p.UserId == userId)));
    }

    private AuctionTopicService Topics(ERecycleHub.Infrastructure.AppDbContext db) =>
        new(db, f.Push, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuctionTopicService>.Instance);

    [Fact]
    public async Task Registering_a_token_subscribes_it_to_running_auctions_the_user_takes_part_in_only()
    {
        var (userId, client) = await SignedInCustomerAsync();
        var running = await f.NewAuctionAsync();
        var ended = await f.NewAuctionAsync(startHours: -48, endHours: -1);
        var other = await f.NewAuctionAsync();
        await JoinAsync(userId, running);
        await JoinAsync(userId, ended);

        var token = NewToken();
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, token, "android", "device-1")).StatusCode);

        Assert.True(f.Push.Did(true, $"auction_{running}", token));
        Assert.False(f.Push.Did(true, $"auction_{ended}", token));
        Assert.False(f.Push.Did(true, $"auction_{other}", token));
        var row = await f.DbAsync(db => db.DeviceTokens.SingleAsync(d => d.Token == token));
        Assert.Equal("android", row.Platform);
        Assert.Equal("device-1", row.DeviceId);
    }

    [Fact]
    public async Task A_new_token_for_the_same_device_replaces_the_old_one()
    {
        var (userId, client) = await SignedInCustomerAsync();
        var auction = await f.NewAuctionAsync();
        await JoinAsync(userId, auction);
        var oldToken = NewToken();
        var newToken = NewToken();
        await RegisterAsync(client, oldToken, "ios", "device-A");
        await RegisterAsync(client, newToken, "ios", "device-A");

        Assert.Equal([newToken], await f.DbAsync(db => db.DeviceTokens.Where(d => d.UserId == userId).Select(d => d.Token).ToListAsync()));
        Assert.True(f.Push.Did(false, $"auction_{auction}", oldToken));
        Assert.True(f.Push.Did(true, $"auction_{auction}", newToken));
    }

    [Fact]
    public async Task A_token_that_moves_to_another_user_leaves_the_previous_users_topics()
    {
        var (firstId, first) = await SignedInCustomerAsync();
        var (_, second) = await SignedInCustomerAsync();
        var auction = await f.NewAuctionAsync();
        await JoinAsync(firstId, auction);
        var token = NewToken();
        await RegisterAsync(first, token);
        await RegisterAsync(second, token);

        Assert.True(f.Push.Did(false, $"auction_{auction}", token));
        Assert.NotEqual(firstId, await f.DbAsync(db => db.DeviceTokens.Where(d => d.Token == token).Select(d => d.UserId).SingleAsync()));
    }

    [Fact]
    public async Task Logout_unsubscribes_the_token_and_then_forgets_it()
    {
        var (userId, client) = await SignedInCustomerAsync();
        var auction = await f.NewAuctionAsync();
        await JoinAsync(userId, auction);
        var token = NewToken();
        await RegisterAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/logout")).StatusCode);
        await f.IdleAsync();

        Assert.True(f.Push.Did(false, $"auction_{auction}", token));
        Assert.False(await f.DbAsync(db => db.DeviceTokens.AnyAsync(d => d.Token == token)));
    }

    [Fact]
    public async Task Dead_tokens_reported_by_the_push_service_are_deleted()
    {
        var (userId, client) = await SignedInCustomerAsync();
        var auction = await f.NewAuctionAsync();
        await JoinAsync(userId, auction);
        var token = NewToken();
        f.Push.DeadTokens.Add(token);
        await RegisterAsync(client, token);

        Assert.False(await f.DbAsync(db => db.DeviceTokens.AnyAsync(d => d.Token == token)));
    }

    [Fact]
    public async Task A_push_outage_never_fails_the_request_and_the_token_is_kept()
    {
        var (userId, client) = await SignedInCustomerAsync();
        await JoinAsync(userId, await f.NewAuctionAsync());
        var token = NewToken();
        f.Push.Throw = true;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, token)).StatusCode);
        }
        finally { f.Push.Throw = false; }
        Assert.True(await f.DbAsync(db => db.DeviceTokens.AnyAsync(d => d.Token == token)));
    }

    [Fact]
    public async Task Lost_subscriptions_are_rebuilt_from_participants_and_tokens()
    {
        var (userId, client) = await SignedInCustomerAsync();
        var auction = await f.NewAuctionAsync();
        var token = NewToken();
        await RegisterAsync(client, token);   // not a participant yet: nothing subscribed
        await JoinAsync(userId, auction);
        Assert.False(f.Push.Did(true, $"auction_{auction}", token));

        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuctionTopicService>().ResyncAllAsync();
        Assert.True(f.Push.Did(true, $"auction_{auction}", token));
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("tok", "windows", null)]
    public async Task Invalid_registrations_are_422(string token, string? platform, string? deviceId)
    {
        var (_, client) = await SignedInCustomerAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/devices/fcm-token", new { token, platform, deviceId })).StatusCode);
    }

    [Fact]
    public async Task Overlong_token_or_device_id_is_422()
    {
        var (_, client) = await SignedInCustomerAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/devices/fcm-token", new { token = new string('x', 451) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/devices/fcm-token", new { token = NewToken(), deviceId = new string('d', 101) })).StatusCode);
    }

    [Fact]
    public async Task Registering_needs_a_bearer_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client().PostAsync("/api/v1/devices/fcm-token", new { token = NewToken() })).StatusCode);
    }
}

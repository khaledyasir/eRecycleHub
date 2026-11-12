using System.Net;
using System.Text.Json;
using ERecycleHub.Application;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>What the website's bid page needs after an auction ends: isWinner / saleStatus on the web details, and the lifecycle events on the SignalR hub.</summary>
[Collection(ApiCollection.Name)]
public class WebAuctionOutcomeTests(ApiFactory f)
{
    /// <summary>Signs in like the browser: the anonymous session's CSRF token, then the cookie login.</summary>
    private async Task<HttpClient> WebSignInAsync(string email)
    {
        var http = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var anon = await http.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password = ApiFactory.Password }) };
        login.Headers.Add("X-CSRF-TOKEN", anon.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(login)).StatusCode);
        return http;
    }

    private static async Task<(bool winner, string? sale)> SaleAsync(HttpClient http, int auction)
    {
        var v = (await http.GetFromJsonAsync<JsonElement>($"/api/auctions/{auction}")).GetProperty("viewer");
        return (v.GetProperty("isWinner").GetBoolean(), v.GetProperty("saleStatus").ValueKind == JsonValueKind.Null ? null : v.GetProperty("saleStatus").GetString());
    }

    /// <summary>Ends the auction by moving its end into the past (the clock stays put, so the cookie sessions stay valid).</summary>
    private Task EndNowAsync(int auction) =>
        f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, DateTime.UtcNow.AddMinutes(-1))));

    [Fact]
    public async Task The_web_details_tell_the_winner_how_the_sale_went()
    {
        f.Flags.On = true;
        try
        {
            var (_, winnerEmail) = await f.NewCustomerAsync(500);
            var (_, otherEmail) = await f.NewCustomerAsync(500);
            var auction = await f.NewAuctionAsync(startHours: -1, endHours: 2, start: 100, increment: 10);
            var app = await f.SignedInAsync(winnerEmail, ApiFactory.Password);
            await app.DataAsync(app.BidAsync(auction, 110));
            var winner = await WebSignInAsync(winnerEmail);
            var other = await WebSignInAsync(otherEmail);

            Assert.Equal((false, (string?)null), await SaleAsync(winner, auction));     // still running

            await EndNowAsync(auction);
            Assert.Equal((true, "Pending"), await SaleAsync(winner, auction));          // the winning bid holds money, the end job has not run yet
            Assert.Equal((false, "Pending"), await SaleAsync(other, auction));

            using (var scope = f.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AuctionClosing>().CloseAsync(auction);
            Assert.Equal((true, "Paid"), await SaleAsync(winner, auction));
            Assert.Equal((false, "Paid"), await SaleAsync(other, auction));
            Assert.Equal((false, (string?)null), await SaleAsync(f.Client().Http, auction));   // anonymous: nothing about the sale
        }
        finally { f.Flags.On = false; }
    }

    [Fact]
    public async Task The_hub_announces_an_extended_and_an_ended_auction_without_the_winner()
    {
        var auction = await f.NewAuctionAsync(startHours: -1, endHours: 2);
        await using var conn = new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/auctions", o =>
            {
                o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var extended = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("auction.extended", e => extended.TrySetResult(e));
        conn.On<JsonElement>("auction.ended", e => ended.TrySetResult(e));
        await conn.StartAsync();
        await conn.InvokeAsync("Watch", auction);

        // The watcher announces changes from the tick after it first saw the auction: keep moving the end until it says so.
        for (var i = 1; i <= 20 && !extended.Task.IsCompleted; i++)
        {
            var end = DateTime.UtcNow.AddHours(2).AddMinutes(i);
            await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, end)));
            await Task.WhenAny(extended.Task, Task.Delay(1000));
        }
        Assert.True(extended.Task.IsCompleted, "auction.extended did not arrive");
        Assert.True((await extended.Task).GetProperty("endDate").GetDateTime() > DateTime.UtcNow);

        await EndNowAsync(auction);
        var data = await ended.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(data.TryGetProperty("finalPrice", out _));
        Assert.False(data.TryGetProperty("winnerUserId", out _));   // the hub is anonymous
    }
}

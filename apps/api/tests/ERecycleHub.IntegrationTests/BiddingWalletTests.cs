using System.Net;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>
/// Bids that only HOLD wallet money (flag on): the wallet balance and ledger are never touched by a bid, a raise, being outbid or a deactivation; money moves only when the
/// auction closes (see AuctionClosingTests). With the flag off nothing is held.
/// </summary>
[Collection(ApiCollection.Name)]
public class BiddingWalletTests(ApiFactory f)
{
    private sealed class FlagScope : IDisposable
    {
        private readonly TestBiddingSettings _flags;
        public FlagScope(TestBiddingSettings flags, bool on) { _flags = flags; flags.On = on; }
        public void Dispose() => _flags.On = false;
    }

    private FlagScope HoldsOn(bool on = true) => new(f.Flags, on);

    private async Task<(int id, ApiClient client)> BidderAsync(decimal wallet = 500)
    {
        var (id, email) = await f.NewCustomerAsync(wallet);
        return (id, await f.SignedInAsync(email, ApiFactory.Password));
    }

    private Task<decimal> BalanceAsync(int userId) => f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync());
    private Task<List<WalletLedgerEntry>> LedgerAsync(int userId) => f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == userId).OrderBy(l => l.Id).ToListAsync());
    private Task<List<Bid>> BidsAsync(int auctionId) => f.DbAsync(db => db.Bids.Where(b => b.AuctionId == auctionId).OrderBy(b => b.Id).ToListAsync());
    private Task<decimal> HeldAsync(int userId) => f.DbAsync(db => db.Bids.Where(b => b.UserId == userId && b.ReleasedAtUtc == null && b.SettledAtUtc == null).SumAsync(b => (decimal?)b.HeldAmount).ContinueWith(t => t.Result ?? 0m));

    private async Task<T> ScopedAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task<Actor> AdminActorAsync() => new(await f.DbAsync(db => db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).FirstAsync()), UserRole.Admin);

    private static async Task<JsonElementWrapper> BalanceDataAsync(ApiClient c) => new(await c.DataAsync(c.GetAsync("/api/v1/wallet/balance")));
    private readonly record struct JsonElementWrapper(System.Text.Json.JsonElement E)
    {
        public decimal Balance => E.GetProperty("balance").GetDecimal();
        public decimal Held => E.GetProperty("held").GetDecimal();
        public decimal Available => E.GetProperty("available").GetDecimal();
    }

    // ---------- flag off: nothing is held ----------

    [Fact]
    public async Task With_the_flag_off_a_bid_holds_nothing_and_needs_no_balance()
    {
        using var _ = HoldsOn(false);
        var (id, client) = await BidderAsync(wallet: 50);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var data = await client.DataAsync(client.BidAsync(auction, 110));
        Assert.Equal(50m, data.GetProperty("wallet").GetDecimal());
        Assert.Equal(0m, (await BidsAsync(auction)).Single().HeldAmount);
        Assert.Single(await LedgerAsync(id));   // only the opening balance
    }

    // ---------- flag on: holds ----------

    [Fact]
    public async Task A_bid_reserves_money_but_never_changes_the_balance_or_the_ledger()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var data = await client.DataAsync(client.BidAsync(auction, 110));

        Assert.Equal(500m, await BalanceAsync(id));
        Assert.Single(await LedgerAsync(id));
        Assert.Equal(390m, data.GetProperty("wallet").GetDecimal());   // the app's wallet figure is the available balance
        var bid = (await BidsAsync(auction)).Single();
        Assert.Equal(110m, bid.HeldAmount);
        Assert.Null(bid.ReleasedAtUtc);
        Assert.Null(bid.SettledAtUtc);
        var balance = await BalanceDataAsync(client);
        Assert.Equal((500m, 110m, 390m), (balance.Balance, balance.Held, balance.Available));
        Assert.Equal(1, await f.DbAsync(db => db.AuctionParticipants.CountAsync(p => p.AuctionId == auction && p.UserId == id)));
    }

    [Fact]
    public async Task Raising_your_own_bid_moves_the_hold_to_the_new_bid()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));
        await client.DataAsync(client.BidAsync(auction, 150));

        var bids = await BidsAsync(auction);
        Assert.NotNull(bids[0].ReleasedAtUtc);
        Assert.Equal(150m, bids[1].HeldAmount);
        Assert.Null(bids[1].ReleasedAtUtc);
        Assert.Equal(150m, await HeldAsync(id));
        Assert.Equal(500m, await BalanceAsync(id));
        Assert.Single(await LedgerAsync(id));
    }

    [Fact]
    public async Task Being_outbid_releases_the_hold_without_any_ledger_entry()
    {
        using var _ = HoldsOn();
        var (a, clientA) = await BidderAsync(500);
        var (b, clientB) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await clientA.DataAsync(clientA.BidAsync(auction, 110));
        await clientB.DataAsync(clientB.BidAsync(auction, 130));

        Assert.Equal(0m, await HeldAsync(a));
        Assert.Equal(130m, await HeldAsync(b));
        Assert.NotNull((await BidsAsync(auction))[0].ReleasedAtUtc);
        Assert.Equal((500m, 500m), (await BalanceAsync(a), await BalanceAsync(b)));
        Assert.Single(await LedgerAsync(a));
        Assert.Single(await LedgerAsync(b));
        var balanceA = await BalanceDataAsync(clientA);
        Assert.Equal(500m, balanceA.Available);
    }

    [Fact]
    public async Task One_user_can_lead_several_auctions_while_the_holds_fit_the_available_balance()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(300);
        var first = await f.NewAuctionAsync(start: 100, increment: 10);
        var second = await f.NewAuctionAsync(start: 100, increment: 10);
        var third = await f.NewAuctionAsync(start: 50, increment: 10);

        await client.DataAsync(client.BidAsync(first, 200));
        var res = await client.BidAsync(second, 110);                      // 110 > 300 - 200 held
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("INSUFFICIENT_WALLET", (await ApiClient.Body(res)).GetProperty("errorCode").GetString());
        await client.DataAsync(client.BidAsync(third, 60));                // fits: 260 held in total

        Assert.Equal(260m, await HeldAsync(id));
        Assert.Equal(40m, (await BalanceDataAsync(client)).Available);
        Assert.Empty(await BidsAsync(second));
    }

    [Fact]
    public async Task A_bid_the_available_balance_cannot_cover_writes_nothing()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(100);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var res = await client.BidAsync(auction, 110);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await ApiClient.Body(res);
        Assert.Equal("INSUFFICIENT_WALLET", body.GetProperty("errorCode").GetString());
        Assert.Contains("110", body.GetProperty("message").GetString());

        Assert.Empty(await BidsAsync(auction));
        Assert.Equal(100m, await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).Select(a => a.CurrentPrice).SingleAsync()));
        Assert.Empty(await f.DbAsync(db => db.AuctionParticipants.Where(p => p.AuctionId == auction).ToListAsync()));
        Assert.Equal(0m, await HeldAsync(id));
    }

    [Fact]
    public async Task A_bid_for_exactly_the_available_balance_works()
    {
        using var _ = HoldsOn();
        var (_, client) = await BidderAsync(110);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));
        Assert.Equal(0m, (await BalanceDataAsync(client)).Available);
    }

    [Fact]
    public async Task The_insurance_rule_is_checked_against_the_available_balance()
    {
        using var _ = HoldsOn();
        var (_, client) = await BidderAsync(300);
        var held = await f.NewAuctionAsync(start: 100, increment: 10);
        var insured = await f.NewAuctionAsync(start: 50, increment: 10, insurance: 150);
        await client.DataAsync(client.BidAsync(held, 200));   // available 100 < insurance 150

        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await client.BidAsync(insured, 60)));
    }

    [Fact]
    public async Task Money_requested_for_withdrawal_cannot_be_bid()
    {
        using var _ = HoldsOn();
        var (_, client) = await BidderAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.PostAsync("/api/v1/wallet/withdraw", new { amount = 100 }));

        Assert.Equal(100m, (await client.DataAsync(client.GetAsync("/api/v1/wallet"))).GetProperty("wallet").GetDecimal());
        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await client.BidAsync(auction, 110)));
    }

    [Fact]
    public async Task Held_money_cannot_be_withdrawn()
    {
        using var _ = HoldsOn();
        var (_, client) = await BidderAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));

        Assert.Equal("INSUFFICIENT_WALLET", await ApiClient.ErrorCodeAsync(await client.PostAsync("/api/v1/wallet/withdraw", new { amount = 100 })));
        await client.DataAsync(client.PostAsync("/api/v1/wallet/withdraw", new { amount = 90 }));
    }

    [Fact]
    public async Task An_administrator_debit_cannot_take_the_balance_below_the_held_money()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));

        async Task DebitAsync(decimal amount) => await ScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await sp.GetRequiredService<WalletService>().DebitAsync(id, amount, LedgerReason.CreditReversed, null, null);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return 0;
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => DebitAsync(100));   // 200 - 100 < 110 held
        Assert.Equal("wallet.insufficient", ex.Code);
        await DebitAsync(90);
        Assert.Equal(110m, await BalanceAsync(id));
    }

    [Fact]
    public async Task Two_simultaneous_bids_by_one_user_in_two_auctions_cannot_over_commit()
    {
        using var _ = HoldsOn();
        for (var round = 0; round < 4; round++)
        {
            var (id, client) = await BidderAsync(250);
            var one = await f.NewAuctionAsync(start: 100, increment: 10);
            var two = await f.NewAuctionAsync(start: 100, increment: 10);

            var results = await Task.WhenAll(client.BidAsync(one, 150), client.BidAsync(two, 150));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
            Assert.Equal(150m, await HeldAsync(id));
        }
    }

    [Fact]
    public async Task A_leader_without_a_hold_is_outbid_with_nothing_to_release()
    {
        using var _ = HoldsOn();
        var (legacy, _) = await f.NewCustomerAsync(500);
        var (b, clientB) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await f.DbAsync(async db =>
        {
            db.Bids.Add(new Bid { AuctionId = auction, UserId = legacy, Amount = 120, HeldAmount = 0, CreatedAtUtc = DateTime.UtcNow });
            await db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.CurrentPrice, 120m).SetProperty(a => a.BidCount, 1));
            await db.SaveChangesAsync();
        });

        await clientB.DataAsync(clientB.BidAsync(auction, 140));

        Assert.Equal(140m, await HeldAsync(b));
        Assert.Equal(0m, await HeldAsync(legacy));
        Assert.Equal(500m, await BalanceAsync(legacy));
    }

    [Fact]
    public async Task Switching_the_flag_never_strands_a_hold()
    {
        var (a, clientA) = await BidderAsync(500);
        var (b, clientB) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        using (HoldsOn()) await clientA.DataAsync(clientA.BidAsync(auction, 110));
        Assert.Equal(110m, await HeldAsync(a));
        await clientB.DataAsync(clientB.BidAsync(auction, 130));   // flag off: B holds nothing, but A's existing hold is still released
        Assert.Equal(0m, await HeldAsync(a));
        Assert.Equal(0m, await HeldAsync(b));

        using (HoldsOn()) await clientA.DataAsync(clientA.BidAsync(auction, 150));
        Assert.Equal(150m, await HeldAsync(a));
        Assert.Equal((500m, 500m), (await BalanceAsync(a), await BalanceAsync(b)));
    }

    [Fact]
    public async Task The_service_reports_what_a_bid_holds_and_releases()
    {
        using var _ = HoldsOn();
        var (a, _) = await f.NewCustomerAsync(500);
        var (b, _) = await f.NewCustomerAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);

        var first = await ScopedAsync(sp => sp.GetRequiredService<BiddingService>().PlaceBidAsync(a, auction, 110));
        var second = await ScopedAsync(sp => sp.GetRequiredService<BiddingService>().PlaceBidAsync(b, auction, 130));

        Assert.Equal((110m, 0m, 390m), (first.Held, first.ReleasedFromPrevious, first.AvailableAfter!.Value));
        Assert.Equal((130m, 110m, 370m), (second.Held, second.ReleasedFromPrevious, second.AvailableAfter!.Value));
    }

    // ---------- atomicity, side effects, concurrency ----------

    [Fact]
    public async Task A_failure_late_in_the_bid_rolls_back_the_bid_the_release_and_the_participant()
    {
        using var _ = HoldsOn();
        var (a, clientA) = await BidderAsync(500);
        var (b, clientB) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await clientA.DataAsync(clientA.BidAsync(auction, 110));
        await f.IdleAsync();   // A's bid queues background notification work; let it finish so it cannot consume the injected failure meant for B's bid

        f.Failure.ArmedOnNotification = true;   // fails when B's bid adds A's "outbid" row, after B's bid row and A's release were written
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await clientB.BidAsync(auction, 120)).StatusCode); }
        finally { f.Failure.ArmedOnNotification = false; }

        var bids = await BidsAsync(auction);
        Assert.Single(bids);
        Assert.Null(bids[0].ReleasedAtUtc);                       // A still holds
        Assert.Equal(110m, await HeldAsync(a));
        Assert.Equal(0m, await HeldAsync(b));
        Assert.Equal(110m, await f.DbAsync(db => db.Auctions.Where(x => x.Id == auction).Select(x => x.CurrentPrice).SingleAsync()));
        Assert.Empty(await f.DbAsync(db => db.AuctionParticipants.Where(p => p.AuctionId == auction && p.UserId == b).ToListAsync()));
    }

    [Fact]
    public async Task The_participant_is_subscribed_after_the_commit_and_never_for_a_rejected_bid()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var token = "tok-" + Guid.NewGuid().ToString("N");
        await client.DataAsync(client.PostAsync("/api/v1/devices/fcm-token", new { token }));
        await f.IdleAsync();

        Assert.Equal("BID_TOO_LOW", await ApiClient.ErrorCodeAsync(await client.BidAsync(auction, 105)));
        await f.IdleAsync();
        Assert.False(f.Push.Did(true, $"auction_{auction}", token));

        await client.DataAsync(client.BidAsync(auction, 110));
        await f.IdleAsync();
        Assert.True(f.Push.Did(true, $"auction_{auction}", token));
        Assert.Equal(1, await f.DbAsync(db => db.AuctionParticipants.CountAsync(p => p.AuctionId == auction && p.UserId == id)));
    }

    [Fact]
    public async Task A_push_outage_does_not_affect_the_bid()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.PostAsync("/api/v1/devices/fcm-token", new { token = "tok-" + Guid.NewGuid().ToString("N") }));
        await f.IdleAsync();
        f.Push.Throw = true;
        try
        {
            await client.DataAsync(client.BidAsync(auction, 110));
            await f.IdleAsync();
        }
        finally { f.Push.Throw = false; }
        Assert.Equal(110m, await HeldAsync(id));
    }

    [Fact]
    public async Task Many_concurrent_bids_across_two_auctions_never_change_balances_never_over_commit_and_never_deadlock()
    {
        using var _ = HoldsOn();
        var users = new List<(int id, ApiClient client)>();
        for (var i = 0; i < 4; i++) users.Add(await BidderAsync(1000));
        var auctions = new[] { await f.NewAuctionAsync(start: 100, increment: 10), await f.NewAuctionAsync(start: 100, increment: 10) };

        var statuses = await Task.WhenAll(users.SelectMany((u, ui) => Enumerable.Range(0, 12).Select(async i =>
        {
            var auction = auctions[(ui + i) % 2];
            var amount = 110 + 10 * (i * 3 + ui);   // overlapping amounts: some win, some lose the race
            return (await u.client.BidAsync(auction, amount)).StatusCode;
        })));

        Assert.All(statuses, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity }));
        Assert.Contains(HttpStatusCode.OK, statuses);
        foreach (var (id, _) in users)
        {
            Assert.Equal(1000m, await BalanceAsync(id));                // bids never touch the balance
            Assert.Single(await LedgerAsync(id));
            Assert.InRange(await HeldAsync(id), 0m, 1000m);
        }
        var report = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
        Assert.DoesNotContain(report.AuctionMismatches, m => auctions.Contains(m.AuctionId));
        Assert.DoesNotContain(report.UserMismatches, m => users.Any(u => u.id == m.UserId));
        Assert.DoesNotContain(report.OverCommitted, m => users.Any(u => u.id == m.UserId));
    }

    // ---------- deactivation, reactivation ----------

    [Fact]
    public async Task Deactivating_an_auction_releases_the_leaders_hold_and_reactivating_reserves_it_again()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));
        var admin = await AdminActorAsync();

        await ScopedAsync(async sp => await sp.GetRequiredService<AuctionManagement>().SetActiveAsync(admin, auction, false));
        Assert.Equal(0m, await HeldAsync(id));
        Assert.NotNull((await BidsAsync(auction)).Single().ReleasedAtUtc);

        await ScopedAsync(async sp => await sp.GetRequiredService<AuctionManagement>().SetActiveAsync(admin, auction, true));
        Assert.Equal(110m, await HeldAsync(id));
        Assert.Null((await BidsAsync(auction)).Single().ReleasedAtUtc);
        Assert.Equal(500m, await BalanceAsync(id));
        Assert.Single(await LedgerAsync(id));   // no refund and no re-debit entries: nothing was ever debited
    }

    [Fact]
    public async Task Reactivation_is_refused_when_the_leader_can_no_longer_cover_the_bid()
    {
        using var _ = HoldsOn();
        var (id, client) = await BidderAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));
        var admin = await AdminActorAsync();
        await ScopedAsync(async sp => await sp.GetRequiredService<AuctionManagement>().SetActiveAsync(admin, auction, false));
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 10m)));

        var ex = await Assert.ThrowsAsync<AppException>(() => ScopedAsync(async sp => await sp.GetRequiredService<AuctionManagement>().SetActiveAsync(admin, auction, true)));
        Assert.Equal("auction.leader_unfunded", ex.Code);
        Assert.False(await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).Select(a => a.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Deactivating_an_ended_auction_releases_an_unpaid_hold_but_never_touches_a_paid_one()
    {
        using var _ = HoldsOn();
        var (unpaid, _) = await f.NewCustomerAsync(500);
        var (paid, _) = await f.NewCustomerAsync(500);
        var auctionA = await f.NewAuctionAsync(startHours: -48, endHours: -1, start: 100, increment: 10);
        var auctionB = await f.NewAuctionAsync(startHours: -48, endHours: -1, start: 100, increment: 10);
        await f.DbAsync(async db =>
        {
            db.Bids.Add(new Bid { AuctionId = auctionA, UserId = unpaid, Amount = 110, HeldAmount = 110, CreatedAtUtc = DateTime.UtcNow.AddHours(-2) });
            db.Bids.Add(new Bid { AuctionId = auctionB, UserId = paid, Amount = 110, HeldAmount = 110, SettledAtUtc = DateTime.UtcNow.AddHours(-1), CreatedAtUtc = DateTime.UtcNow.AddHours(-2) });
            await db.SaveChangesAsync();
        });
        var admin = await AdminActorAsync();

        await ScopedAsync(async sp => { var m = sp.GetRequiredService<AuctionManagement>(); await m.SetActiveAsync(admin, auctionA, false); await m.SetActiveAsync(admin, auctionB, false); return 0; });

        Assert.NotNull((await BidsAsync(auctionA)).Single().ReleasedAtUtc);
        Assert.Null((await BidsAsync(auctionB)).Single().ReleasedAtUtc);
    }

    [Fact]
    public async Task Deactivating_a_seller_releases_the_holds_in_their_auctions_and_reactivating_reserves_them_again()
    {
        using var _ = HoldsOn();
        var publisherId = await f.PublisherIdAsync(DevSeed.PublisherEmail);
        var sellerUserId = await f.DbAsync(db => db.Publishers.Where(p => p.Id == publisherId).Select(p => p.UserId).SingleAsync());
        var auction = await f.NewAuctionAsync(publisherId: publisherId, start: 100, increment: 10);
        var (id, client) = await BidderAsync(500);
        await client.DataAsync(client.BidAsync(auction, 110));
        var admin = await AdminActorAsync();

        try
        {
            await ScopedAsync(async sp => { await sp.GetRequiredService<AdminPeople>().SetUserActiveAsync(admin, sellerUserId, false); return 0; });
            Assert.Equal(0m, await HeldAsync(id));
        }
        finally
        {
            await ScopedAsync(async sp => { await sp.GetRequiredService<AdminPeople>().SetUserActiveAsync(admin, sellerUserId, true); return 0; });
        }
        Assert.Equal(110m, await HeldAsync(id));
        Assert.Equal(500m, await BalanceAsync(id));
    }

    // ---------- the consistency check ----------

    [Fact]
    public async Task The_consistency_check_passes_for_a_normal_history_and_flags_tampering()
    {
        using var _ = HoldsOn();
        var (a, clientA) = await BidderAsync(500);
        var (b, clientB) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await clientA.DataAsync(clientA.BidAsync(auction, 110));
        await clientB.DataAsync(clientB.BidAsync(auction, 130));
        await clientB.DataAsync(clientB.BidAsync(auction, 150));

        var clean = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
        Assert.DoesNotContain(clean.UserMismatches, m => m.UserId == a || m.UserId == b);
        Assert.DoesNotContain(clean.OverCommitted, m => m.UserId == a || m.UserId == b);
        Assert.DoesNotContain(clean.AuctionMismatches, m => m.AuctionId == auction);

        await f.DbAsync(db => db.Users.Where(u => u.Id == a).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 505m)));          // balance no longer matches the ledger
        await f.DbAsync(db => db.Bids.Where(x => x.AuctionId == auction && x.UserId == a).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReleasedAtUtc, (DateTime?)null)));   // a second holding bid
        await f.DbAsync(db => db.Users.Where(u => u.Id == b).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 100m)));        // balance below the held money
        var broken = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());

        Assert.False(broken.Ok);
        Assert.Contains(broken.UserMismatches, m => m.UserId == a && m.Difference == 5m);
        Assert.Contains(broken.AuctionMismatches, m => m.AuctionId == auction && m.HoldingBids == 2);
        Assert.Contains(broken.OverCommitted, m => m.UserId == b);
    }

    // ---------- bids held under the old debit-at-bid model ----------

    [Fact]
    public async Task The_conversion_turns_old_debited_bids_into_holds_and_marks_paid_winners_settled()
    {
        var (id, _) = await f.NewCustomerAsync(500);   // opening ledger +500
        var running = await f.NewAuctionAsync(start: 100, increment: 10);
        var ended = await f.NewAuctionAsync(startHours: -48, endHours: -1, start: 100, increment: 10);
        var (runningBid, endedBid) = await f.DbAsync(async db =>
        {
            var r = new Bid { AuctionId = running, UserId = id, Amount = 110, HeldAmount = 110, CreatedAtUtc = DateTime.UtcNow };
            var e = new Bid { AuctionId = ended, UserId = id, Amount = 130, HeldAmount = 130, CreatedAtUtc = DateTime.UtcNow.AddHours(-3) };
            db.Bids.AddRange(r, e);
            await db.SaveChangesAsync();
            // the old model had debited both: balance 500 - 110 - 130
            db.WalletLedger.Add(new WalletLedgerEntry { UserId = id, Amount = -110, BalanceBefore = 500, BalanceAfter = 390, Reason = LedgerReason.BidDeduction, ReferenceType = "bid", ReferenceId = r.Id, CreatedAtUtc = DateTime.UtcNow });
            db.WalletLedger.Add(new WalletLedgerEntry { UserId = id, Amount = -130, BalanceBefore = 390, BalanceAfter = 260, Reason = LedgerReason.BidDeduction, ReferenceType = "bid", ReferenceId = e.Id, CreatedAtUtc = DateTime.UtcNow });
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 260m));
            await db.SaveChangesAsync();
            return (r.Id, e.Id);
        });

        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync(HoldModelConversion.Script));
        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync(HoldModelConversion.Script));   // a second run changes nothing

        Assert.Equal(370m, await BalanceAsync(id));   // the 110 on the running auction is a hold again; the 130 on the ended one was a payment
        var conversion = (await LedgerAsync(id)).Where(l => l.Reason == LedgerReason.HoldConversion).ToList();
        Assert.Equal(110m, conversion.Single().Amount);
        Assert.Null((await f.DbAsync(db => db.Bids.SingleAsync(b => b.Id == runningBid))).SettledAtUtc);
        Assert.NotNull((await f.DbAsync(db => db.Bids.SingleAsync(b => b.Id == endedBid))).SettledAtUtc);
        Assert.Equal(110m, await HeldAsync(id));
        var report = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
        Assert.DoesNotContain(report.UserMismatches, m => m.UserId == id);
        Assert.DoesNotContain(report.SettlementMismatches, m => m.BidId == endedBid);
    }

    [Fact]
    public async Task The_conversion_does_nothing_for_users_who_never_went_through_the_old_model()
    {
        var (id, _) = await f.NewCustomerAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await f.DbAsync(async db => { db.Bids.Add(new Bid { AuctionId = auction, UserId = id, Amount = 110, HeldAmount = 110, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); });

        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync(HoldModelConversion.Script));

        Assert.Equal(500m, await BalanceAsync(id));
        Assert.Single(await LedgerAsync(id));
    }

    // ---------- display-only hold lines in the wallet history ----------

    [Fact]
    public async Task Wallet_history_shows_hold_and_release_lines_built_from_the_bids_without_touching_money()
    {
        using var _ = HoldsOn();
        var (aId, a) = await BidderAsync(500);
        var (_, b) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var ledgerBefore = (await LedgerAsync(aId)).Count;

        await a.DataAsync(a.BidAsync(auction, 110));
        await a.DataAsync(a.BidAsync(auction, 130));   // raise: the 110 hold is released, 130 is held
        await b.DataAsync(b.BidAsync(auction, 150));   // outbid: A's 130 is released

        var w = await a.DataAsync(a.GetAsync("/api/v1/wallet"));
        var lines = w.GetProperty("transaction").EnumerateArray().ToList();
        string Type(System.Text.Json.JsonElement t) => t.GetProperty("type").GetString()!;
        Assert.Equal(2, lines.Count(t => Type(t) == "Hold" && t.GetProperty("amount").GetDecimal() is 110 or 130));
        Assert.Equal(2, lines.Count(t => Type(t) == "HoldRelease"));
        Assert.All(lines.Where(t => Type(t) is "Hold" or "HoldRelease"), t =>
        {
            Assert.True(t.GetProperty("id").GetInt32() < 0);
            Assert.Equal(auction, t.GetProperty("auctionId").GetInt32());
            Assert.False(string.IsNullOrEmpty(t.GetProperty("auctionTitle").GetString()));
        });
        Assert.Equal(lines.Count, lines.Select(t => t.GetProperty("id").GetInt32()).Distinct().Count());   // ids never clash

        // Display only: balance, ledger and holds are exactly what the hold model says.
        Assert.Equal(500m, w.GetProperty("wallet").GetDecimal());
        Assert.Equal(ledgerBefore, (await LedgerAsync(aId)).Count);
        Assert.Equal(500m, await BalanceAsync(aId));
        Assert.Equal(0m, await HeldAsync(aId));
    }

    [Fact]
    public async Task Wallet_history_has_no_hold_lines_when_the_flag_was_off()
    {
        using var _ = HoldsOn(false);
        var (_, a) = await BidderAsync(500);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await a.DataAsync(a.BidAsync(auction, 110));
        var w = await a.DataAsync(a.GetAsync("/api/v1/wallet"));
        Assert.DoesNotContain(w.GetProperty("transaction").EnumerateArray(), t => t.GetProperty("type").GetString() is "Hold" or "HoldRelease");
    }
}

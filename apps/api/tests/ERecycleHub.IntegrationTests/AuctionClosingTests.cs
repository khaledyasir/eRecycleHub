using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>Phase 4: closing an ended auction. The winner's hold becomes a payment exactly once, in one transaction with the auction row locked first, and the participants are told.</summary>
[Collection(ApiCollection.Name)]
public class AuctionClosingTests(ApiFactory f)
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
    private Task<List<WalletLedgerEntry>> LedgerAsync(int userId, LedgerReason? reason = null) =>
        f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == userId && (reason == null || l.Reason == reason)).OrderBy(l => l.Id).ToListAsync());
    private Task<List<Bid>> BidsAsync(int auctionId) => f.DbAsync(db => db.Bids.Where(b => b.AuctionId == auctionId).OrderBy(b => b.Id).ToListAsync());
    private Task<Auction> AuctionAsync(int id) => f.DbAsync(db => db.Auctions.AsNoTracking().SingleAsync(a => a.Id == id));
    private Task<List<Notification>> RowsAsync(int auctionId) => f.DbAsync(db => db.Notifications.Where(n => n.AuctionId == auctionId).OrderBy(n => n.Id).ToListAsync());

    private async Task<T> ScopedAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<CloseResult> CloseAsync(int auctionId) => ScopedAsync(sp => sp.GetRequiredService<AuctionClosing>().CloseAsync(auctionId));

    /// <summary>A live auction (ends in 2 hours) that this test can end by moving the clock; always put back.</summary>
    private Task<int> LiveAuctionAsync(decimal start = 100, decimal increment = 10) => f.NewAuctionAsync(startHours: -1, endHours: 2, start: start, increment: increment);

    private sealed class TimeTravel : IDisposable
    {
        private readonly TestClock _clock;
        public TimeTravel(TestClock clock, TimeSpan by) { _clock = clock; clock.Offset = by; }
        public void Dispose() => _clock.Offset = TimeSpan.Zero;
    }

    private TimeTravel PastTheEnd() => new(f.Clock, TimeSpan.FromHours(3));

    private Task<decimal> ActiveHoldsAsync(int userId) =>
        f.DbAsync(db => db.Bids.Where(b => b.UserId == userId && b.ReleasedAtUtc == null && b.SettledAtUtc == null).SumAsync(b => (decimal?)b.HeldAmount)).ContinueWith(t => t.Result ?? 0m);

    // ---------- the payment ----------

    [Fact]
    public async Task The_winners_hold_becomes_one_payment_and_the_participants_are_told()
    {
        using var _ = HoldsOn();
        var (winner, clientW) = await BidderAsync(500);
        var (loser, clientL) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await clientL.DataAsync(clientL.BidAsync(auction, 110));
        await clientW.DataAsync(clientW.BidAsync(auction, 130));

        CloseResult result;
        using (PastTheEnd()) result = await CloseAsync(auction);

        Assert.Equal(CloseOutcome.Settled, result.Outcome);
        Assert.Equal(winner, result.WinnerUserId);
        Assert.Equal(130m, result.Charged);
        Assert.Equal(370m, await BalanceAsync(winner));
        Assert.Equal(500m, await BalanceAsync(loser));
        var payment = (await LedgerAsync(winner, LedgerReason.AuctionPayment)).Single();
        var winningBid = (await BidsAsync(auction)).Single(b => b.Amount == 130);
        Assert.Equal((-130m, 500m, 370m, LedgerReferences.Bid, (long?)winningBid.Id), (payment.Amount, payment.BalanceBefore, payment.BalanceAfter, payment.ReferenceType, payment.ReferenceId));
        Assert.NotNull(winningBid.SettledAtUtc);
        Assert.Empty(await LedgerAsync(loser, LedgerReason.AuctionPayment));
        { var ended = await AuctionAsync(auction); Assert.NotNull(ended.EndProcessedAtUtc); Assert.Null(ended.WinnerNotifiedAtUtc); }

        var rows = await RowsAsync(auction);
        Assert.Equal(NotificationKinds.AuctionWon, rows.Single(n => n.UserId == winner && n.EventKey == $"end:{auction}").Kind);
        Assert.Equal(NotificationKinds.Ended, rows.Single(n => n.UserId == loser && n.EventKey == $"end:{auction}").Kind);
        var balance = await clientW.DataAsync(clientW.GetAsync("/api/v1/wallet/balance"));
        Assert.Equal((370m, 0m, 370m), (balance.GetProperty("balance").GetDecimal(), balance.GetProperty("held").GetDecimal(), balance.GetProperty("available").GetDecimal()));
    }

    [Fact]
    public async Task Closing_twice_or_at_the_same_time_charges_once()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));

        CloseResult[] results;
        using (PastTheEnd())
        {
            results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CloseAsync(auction)));
            results = [.. results, await CloseAsync(auction)];
        }

        Assert.Equal(1, results.Count(r => r.Outcome == CloseOutcome.Settled));
        Assert.All(results.Where(r => r.Outcome != CloseOutcome.Settled), r => Assert.Equal(CloseOutcome.Skipped, r.Outcome));
        Assert.Single(await LedgerAsync(winner, LedgerReason.AuctionPayment));
        Assert.Equal(390m, await BalanceAsync(winner));
        Assert.Single(await RowsAsync(auction), n => n.UserId == winner && n.Kind == NotificationKinds.AuctionWon);
    }

    [Fact]
    public async Task A_winning_bid_that_holds_nothing_is_announced_but_not_charged()
    {
        using var _ = HoldsOn(false);
        var (winner, client) = await BidderAsync(50);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));

        CloseResult result;
        using (PastTheEnd()) result = await CloseAsync(auction);

        Assert.Equal(CloseOutcome.NoHold, result.Outcome);
        Assert.Equal(50m, await BalanceAsync(winner));
        Assert.Empty(await LedgerAsync(winner, LedgerReason.AuctionPayment));
        { var ended = await AuctionAsync(auction); Assert.NotNull(ended.EndProcessedAtUtc); Assert.Null(ended.WinnerNotifiedAtUtc); }
        Assert.Single(await RowsAsync(auction), n => n.UserId == winner && n.Kind == NotificationKinds.AuctionWon);
    }

    [Fact]
    public async Task Auctions_that_are_not_ready_are_left_alone()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var running = await LiveAuctionAsync();
        var noBids = await f.NewAuctionAsync(startHours: -48, endHours: -1);
        var deactivated = await f.NewAuctionAsync(startHours: -48, endHours: -1, active: false);
        await client.DataAsync(client.BidAsync(running, 110));

        Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(running)).Outcome);        // still running
        Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(noBids)).Outcome);
        Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(deactivated)).Outcome);
        Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(int.MaxValue)).Outcome);   // unknown id
        Assert.Equal(500m, await BalanceAsync(winner));
        Assert.Null((await BidsAsync(running)).Single().SettledAtUtc);
    }

    [Fact]
    public async Task An_auction_extended_after_it_was_selected_is_not_closed()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));

        using (PastTheEnd())
        {
            await f.DbAsync(db => db.Auctions.Where(a => a.Id == auction).ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, DateTime.UtcNow.AddHours(10))));   // an admin extended it
            Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(auction)).Outcome);
        }
        Assert.Equal(500m, await BalanceAsync(winner));
    }

    // ---------- the winner cannot pay ----------

    [Fact]
    public async Task A_winner_who_cannot_pay_changes_nothing_is_marked_once_and_settles_after_a_top_up()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));
        await f.DbAsync(db => db.Users.Where(u => u.Id == winner).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 50m)));   // e.g. an administrator debited them after the bid

        using (PastTheEnd())
        {
            Assert.Equal(CloseOutcome.PaymentFailed, (await CloseAsync(auction)).Outcome);
            Assert.Equal(CloseOutcome.PaymentFailed, (await CloseAsync(auction)).Outcome);   // the job retries every tick

            Assert.Equal(50m, await BalanceAsync(winner));
            Assert.Empty(await LedgerAsync(winner, LedgerReason.AuctionPayment));
            var bid = (await BidsAsync(auction)).Single();
            Assert.Null(bid.SettledAtUtc);
            Assert.Equal(110m, bid.HeldAmount);
            var failed = await AuctionAsync(auction);
            Assert.NotNull(failed.SettlementFailedAtUtc);
            Assert.Null(failed.EndProcessedAtUtc);
            var rows = await RowsAsync(auction);
            Assert.Single(rows, n => n.Kind == NotificationKinds.PaymentFailed && n.UserId == winner);   // told once
            Assert.DoesNotContain(rows, n => n.Kind == NotificationKinds.AuctionWon);

            var listed = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().UnsettledAsync(failedOnly: true));
            var row = Assert.Single(listed, u => u.AuctionId == auction);
            Assert.Equal((winner, 110m, 50m, 60m), (row.WinnerUserId, row.Amount, row.WinnerBalance, row.Shortfall));
            var report = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
            Assert.Contains(report.UnsettledWarnings, u => u.AuctionId == auction);
            Assert.Contains(report.OverCommitted, o => o.UserId == winner);   // the books say it too: 50 < 110 held

            await f.DbAsync(db => db.Users.Where(u => u.Id == winner).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 200m)));   // topped up
            Assert.Equal(CloseOutcome.Settled, (await CloseAsync(auction)).Outcome);
        }
        Assert.Equal(90m, await BalanceAsync(winner));
        Assert.Null((await AuctionAsync(auction)).SettlementFailedAtUtc);
        Assert.Single(await RowsAsync(auction), n => n.Kind == NotificationKinds.AuctionWon);
        var stillListed = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().UnsettledAsync(failedOnly: true));
        Assert.DoesNotContain(stillListed, u => u.AuctionId == auction);
    }

    [Fact]
    public async Task An_administrator_can_void_a_sale_that_cannot_be_paid()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));
        await f.DbAsync(db => db.Users.Where(u => u.Id == winner).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 50m)));
        var admin = new Actor(await f.DbAsync(db => db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).FirstAsync()), UserRole.Admin);

        using (PastTheEnd())
        {
            await CloseAsync(auction);
            await ScopedAsync(async sp => { await sp.GetRequiredService<AuctionClosing>().VoidSaleAsync(admin, auction); return 0; });

            Assert.NotNull((await BidsAsync(auction)).Single().ReleasedAtUtc);
            var audit = await f.DbAsync(db => db.AuditLogs.Where(l => l.Action == "auction.void_sale" && l.EntityId == auction.ToString()).ToListAsync());
            Assert.Equal(admin.UserId, Assert.Single(audit).ActorUserId);
            Assert.Equal(0m, await ActiveHoldsAsync(winner));
            Assert.Equal(CloseOutcome.Skipped, (await CloseAsync(auction)).Outcome);   // no winner, no charge, no announcement
            var again = await Assert.ThrowsAsync<AppException>(() => ScopedAsync(async sp => { await sp.GetRequiredService<AuctionClosing>().VoidSaleAsync(admin, auction); return 0; }));
            Assert.Equal("auction.no_unsettled_sale", again.Code);
        }
        Assert.Equal(50m, await BalanceAsync(winner));
        Assert.Empty(await LedgerAsync(winner, LedgerReason.AuctionPayment));
    }

    [Fact]
    public async Task Only_an_administrator_can_void_a_sale()
    {
        var (_, client) = await BidderAsync(500);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsync("/api/admin/auctions/1/void-sale")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/unsettled-auctions")).StatusCode);
        var anonymous = new ApiClient(f.CreateClient());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/admin/auctions/1/void-sale")).StatusCode);
    }

    // ---------- after the commit ----------

    [Fact]
    public async Task The_topic_hears_that_it_ended_and_the_winner_gets_their_own_push_with_the_charge()
    {
        using var _ = HoldsOn();
        var (winner, clientW) = await BidderAsync(500);
        var (loser, clientL) = await BidderAsync(500);
        var winnerToken = "tok-" + Guid.NewGuid().ToString("N");
        var loserToken = "tok-" + Guid.NewGuid().ToString("N");
        await clientW.DataAsync(clientW.PostAsync("/api/v1/devices/fcm-token", new { token = winnerToken }));
        await clientL.DataAsync(clientL.PostAsync("/api/v1/devices/fcm-token", new { token = loserToken }));
        var auction = await LiveAuctionAsync();
        await clientL.DataAsync(clientL.BidAsync(auction, 110));
        await clientW.DataAsync(clientW.BidAsync(auction, 130));
        await f.IdleAsync();
        var before = f.Push.Sent.Count;

        using (PastTheEnd()) await CloseAsync(auction);
        await f.IdleAsync();

        var topic = Assert.Single(f.Push.TopicSent, t => t.Topic == $"auction_{auction}" && t.Message.Type == "auction_ended");
        Assert.Equal($"auction_{auction}_end", topic.Message.CollapseKey);
        var won = Assert.Single(f.Push.Sent.Skip(before), s => s.Token == winnerToken);
        Assert.Equal("auction_won", won.Message.Type);
        Assert.Equal("130.00", won.Message.Data["charged"]);
        Assert.Equal(topic.Message.CollapseKey, won.Message.CollapseKey);     // the device shows one notification
        Assert.DoesNotContain(f.Push.Sent.Skip(before), s => s.Token == loserToken);   // the loser is covered by the topic message
        var row = (await RowsAsync(auction)).Single(n => n.UserId == winner && n.Kind == NotificationKinds.AuctionWon);
        Assert.Equal(row.Id.ToString(), won.Message.Data["notification_id"]);
        Assert.Equal(auction.ToString(), won.Message.Data["auction_id"]);
    }

    [Fact]
    public async Task The_end_job_sends_no_e_mail_and_leaves_WinnerNotifiedAtUtc_to_the_admin_flow()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var email = await f.DbAsync(db => db.Users.Where(u => u.Id == winner).Select(u => u.Email).SingleAsync());
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));

        using (PastTheEnd()) { await CloseAsync(auction); await CloseAsync(auction); }
        await f.IdleAsync();

        var mailDir = Path.Combine(f.DataRoot, "mail");
        var sent = Directory.Exists(mailDir) ? Directory.GetFiles(mailDir, "*.html").Count(file => File.ReadAllText(file).Contains($"To: {email}")) : 0;
        Assert.Equal(0, sent);
        var ended = await AuctionAsync(auction);
        Assert.NotNull(ended.EndProcessedAtUtc);
        Assert.Null(ended.WinnerNotifiedAtUtc);   // still unclaimed, so the admin "notify winners" button can send its e-mail
    }

    [Fact]
    public async Task A_push_outage_does_not_undo_the_close()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        await client.DataAsync(client.PostAsync("/api/v1/devices/fcm-token", new { token = "tok-" + Guid.NewGuid().ToString("N") }));
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));
        await f.IdleAsync();

        f.Push.Throw = true;
        try
        {
            using (PastTheEnd()) Assert.Equal(CloseOutcome.Settled, (await CloseAsync(auction)).Outcome);
            await f.IdleAsync();
        }
        finally { f.Push.Throw = false; }
        Assert.Equal(390m, await BalanceAsync(winner));
        { var ended = await AuctionAsync(auction); Assert.NotNull(ended.EndProcessedAtUtc); Assert.Null(ended.WinnerNotifiedAtUtc); }
        Assert.Single(await RowsAsync(auction), n => n.Kind == NotificationKinds.AuctionWon);
    }

    // ---------- the books afterwards ----------

    [Fact]
    public async Task After_closing_the_books_balance_and_the_payment_counts_as_platform_revenue()
    {
        using var _ = HoldsOn();
        var (winner, client) = await BidderAsync(500);
        var auction = await LiveAuctionAsync();
        await client.DataAsync(client.BidAsync(auction, 110));
        using (PastTheEnd()) await CloseAsync(auction);

        var report = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
        Assert.DoesNotContain(report.UserMismatches, m => m.UserId == winner);
        Assert.DoesNotContain(report.SettlementMismatches, m => m.AuctionId == auction);
        Assert.DoesNotContain(report.AuctionMismatches, m => m.AuctionId == auction);
        Assert.True(report.PlatformRevenue >= 110m);
        Assert.True(report.PaymentsChecked >= 1);

        // tamper: remove the payment entry and the check notices a settled bid without one
        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Users SET WalletBalance = WalletBalance + 110 WHERE Id = {0}", winner));
        await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == winner && l.Reason == LedgerReason.AuctionPayment).ExecuteDeleteAsync());
        var broken = await ScopedAsync(sp => sp.GetRequiredService<WalletConsistency>().CheckAsync());
        Assert.Contains(broken.SettlementMismatches, m => m.AuctionId == auction);
    }
}

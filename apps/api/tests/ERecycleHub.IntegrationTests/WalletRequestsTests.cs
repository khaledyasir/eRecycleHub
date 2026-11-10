using System.Net;
using System.Net.Http.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>
/// Phase 5: wallet top-up and withdrawal requests and their approval. An approval is atomic and idempotent, uses the same available-balance guard as bids and
/// admin debits, and notifies the user once with the Phase 4 pipeline (in-app row with an event key, then one push per device).
/// </summary>
[Collection(ApiCollection.Name)]
public class WalletRequestsTests(ApiFactory f)
{
    private sealed class FlagScope : IDisposable
    {
        private readonly TestBiddingSettings _flags;
        public FlagScope(TestBiddingSettings flags, bool on) { _flags = flags; flags.On = on; }
        public void Dispose() => _flags.On = false;
    }

    private static string NewToken() => "tok-" + Guid.NewGuid().ToString("N");

    private async Task<(int id, ApiClient client, string token)> UserAsync(decimal wallet = 500, string? lang = "en")
    {
        var (id, email) = await f.NewCustomerAsync(wallet);
        var client = await f.SignedInAsync(email, ApiFactory.Password);
        if (lang is not null) client.Http.DefaultRequestHeaders.Add("lang", lang);
        var token = NewToken();
        await client.DataAsync(client.PostAsync("/api/v1/devices/fcm-token", new { token }));
        await f.IdleAsync();
        return (id, client, token);
    }

    private async Task<Actor> AdminAsync() => new(await f.DbAsync(db => db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).FirstAsync()), UserRole.Admin);

    private async Task<T> WalletAsync<T>(Func<WalletService, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<WalletService>());
    }

    private static Task<HttpResponseMessage> PostWithKeyAsync(ApiClient c, string url, decimal amount, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { amount }, options: ApiClient.Json) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return c.Http.SendAsync(req);
    }

    private async Task<int> RequestAsync(ApiClient c, string kind, decimal amount, string? key = null)
    {
        var r = await PostWithKeyAsync(c, $"/api/v1/wallet/{kind}", amount, key);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var userId = await f.DbAsync(db => db.WalletTransactions.OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync());
        return userId;
    }

    private Task<decimal> BalanceAsync(int userId) => f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync());
    private Task<List<WalletLedgerEntry>> LedgerAsync(int userId, LedgerReason reason) => f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == userId && l.Reason == reason).ToListAsync());
    private Task<List<Notification>> RowsAsync(int txId) => f.DbAsync(db => db.Notifications.Where(n => n.EventKey!.StartsWith("wtx:" + txId + ":")).ToListAsync());
    private Task<WalletTransactionStatus> StatusAsync(int txId) => f.DbAsync(db => db.WalletTransactions.Where(t => t.Id == txId).Select(t => t.Status).SingleAsync());
    private IEnumerable<PushTarget> PushesTo(string token) => f.Push.Sent.Where(s => s.Token == token);

    // ---------- top-ups ----------

    [Fact]
    public async Task A_top_up_approval_credits_once_even_when_approved_many_times_at_once_and_notifies_once()
    {
        var (id, client, token) = await UserAsync(100);
        var tx = await RequestAsync(client, "increase", 40m);
        var admin = await AdminAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await WalletAsync(w => w.ApproveAsync(admin, tx)); return "ok"; }
            catch (AppException ex) { return ex.Code; }
        }));
        await f.IdleAsync();

        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.All(results.Where(r => r != "ok"), r => Assert.Equal("wallet.not_pending", r));
        Assert.Equal(140m, await BalanceAsync(id));
        var ledger = Assert.Single(await LedgerAsync(id, LedgerReason.TopUpApproved));
        Assert.Equal(40m, ledger.Amount);
        var row = Assert.Single(await RowsAsync(tx));
        Assert.Equal($"wtx:{tx}:approved", row.EventKey);
        Assert.Equal(NotificationKinds.TopUpApproved, row.Kind);
        Assert.Equal(40m, row.Amount);
        Assert.NotNull(row.PushedAtUtc);

        var push = Assert.Single(PushesTo(token), p => p.Message.Type == "topup_approved");
        Assert.Null(push.Message.AuctionId);
        Assert.False(push.Message.Data.ContainsKey("auction_id"));
        Assert.Equal(tx.ToString(), push.Message.Data["transaction_id"]);
        Assert.Equal(row.Id.ToString(), push.Message.Data["notification_id"]);
        Assert.Equal("40.00", push.Message.Data["amount"]);
        Assert.Equal(push.Message.Data["title_en"], push.Message.Title);   // the device registered with lang: en

        // The job running again (a retry, a restart) sends nothing more.
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WalletNotifier>().NotifyDecisionAsync(tx, WalletTransactionStatus.Approved);
        Assert.Single(PushesTo(token), p => p.Message.Type == "topup_approved");
    }

    [Fact]
    public async Task A_rejected_top_up_moves_no_money_notifies_once_and_cannot_then_be_approved()
    {
        var (id, client, token) = await UserAsync(100, lang: null);   // no lang header: Arabic by default
        var tx = await RequestAsync(client, "increase", 25m);
        var admin = await AdminAsync();

        await WalletAsync(w => w.RejectAsync(admin, tx));
        await f.IdleAsync();
        var again = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ApproveAsync(admin, tx)));

        Assert.Equal("wallet.not_pending", again.Code);
        Assert.Equal(WalletTransactionStatus.Rejected, await StatusAsync(tx));
        Assert.Equal(100m, await BalanceAsync(id));
        Assert.Empty(await LedgerAsync(id, LedgerReason.TopUpApproved));
        var row = Assert.Single(await RowsAsync(tx));
        Assert.Equal(NotificationKinds.TopUpRejected, row.Kind);
        var push = Assert.Single(PushesTo(token));
        Assert.Equal("topup_rejected", push.Message.Type);
        Assert.Equal(push.Message.Data["title_ar"], push.Message.Title);
    }

    // ---------- withdrawals ----------

    [Fact]
    public async Task A_withdrawal_approval_debits_once_and_notifies_once()
    {
        var (id, client, token) = await UserAsync(300);
        var tx = await RequestAsync(client, "withdraw", 120m);
        var admin = await AdminAsync();
        Assert.Equal(300m, await BalanceAsync(id));   // a request never touches the balance

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            try { await WalletAsync(w => w.ApproveAsync(admin, tx)); return "ok"; }
            catch (AppException ex) { return ex.Code; }
        }));
        await f.IdleAsync();

        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(180m, await BalanceAsync(id));
        var ledger = Assert.Single(await LedgerAsync(id, LedgerReason.WithdrawalPaid));
        Assert.Equal(-120m, ledger.Amount);
        Assert.Equal(LedgerReferences.WalletTransaction, ledger.ReferenceType);
        Assert.Equal(NotificationKinds.WithdrawalApproved, Assert.Single(await RowsAsync(tx)).Kind);
        Assert.Equal("withdrawal_approved", Assert.Single(PushesTo(token)).Message.Type);
    }

    [Fact]
    public async Task A_withdrawal_approval_is_refused_when_the_available_balance_no_longer_covers_it_and_succeeds_once_it_does()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (id, client, token) = await UserAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));                 // holds 110 of the 200
        var tx = await RequestAsync(client, "withdraw", 80m);                  // 200 - 110 held = 90 available: accepted
        var admin = await AdminAsync();
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 150m)));   // the balance dropped meanwhile

        var refused = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ApproveAsync(admin, tx)));   // 150 - 80 = 70 < 110 held
        await f.IdleAsync();

        Assert.Equal("wallet.insufficient", refused.Code);
        Assert.Equal(422, refused.Status);
        Assert.NotNull(refused.Args);
        Assert.Equal(WalletTransactionStatus.Pending, await StatusAsync(tx));
        Assert.Equal(150m, await BalanceAsync(id));
        Assert.Empty(await LedgerAsync(id, LedgerReason.WithdrawalPaid));
        Assert.Empty(await RowsAsync(tx));
        Assert.DoesNotContain(f.Push.Sent, s => s.Token == token && s.Message.Type == "withdrawal_approved");

        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 200m)));
        await WalletAsync(w => w.ApproveAsync(admin, tx));                     // 200 - 80 = 120 >= 110 held
        Assert.Equal(120m, await BalanceAsync(id));
        Assert.Equal(WalletTransactionStatus.Approved, await StatusAsync(tx));
    }

    // ---------- reversal ----------

    [Fact]
    public async Task A_credit_cannot_be_reversed_after_the_money_was_spent_and_the_reversal_notifies_once()
    {
        var (id, _, token) = await UserAsync(0);
        var admin = await AdminAsync();
        var credit = (await WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 50m, null, null, null))).Id;
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 10m)));   // 40 was spent

        var refused = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ReverseAsync(admin, credit)));
        Assert.Equal("wallet.insufficient", refused.Code);
        Assert.Equal(WalletTransactionStatus.Approved, await StatusAsync(credit));
        Assert.Equal(10m, await BalanceAsync(id));

        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 60m)));
        await WalletAsync(w => w.ReverseAsync(admin, credit));
        await f.IdleAsync();

        Assert.Equal(10m, await BalanceAsync(id));
        Assert.Equal(WalletTransactionStatus.Reversed, await StatusAsync(credit));
        var row = Assert.Single(await RowsAsync(credit), r => r.Kind == NotificationKinds.CreditReversed);
        Assert.Equal($"wtx:{credit}:reversed", row.EventKey);
        Assert.Equal("credit_reversed", Assert.Single(PushesTo(token)).Message.Type);
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Approvals_racing_bids_and_new_requests_for_one_user_never_deadlock_over_commit_or_unbalance_the_books()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (id, client, _) = await UserAsync(1000);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        var pending = new List<int>();
        for (var i = 0; i < 5; i++) pending.Add(await RequestAsync(client, "withdraw", 50m));
        var admin = await AdminAsync();

        var work = new List<Task<int>>();
        work.AddRange(pending.Select(tx => Task.Run(async () =>
        {
            try { await WalletAsync(w => w.ApproveAsync(admin, tx)); return 200; }
            catch (AppException ex) { return ex.Status; }   // refused by the guard: fine
        })));
        for (var i = 0; i < 4; i++)
        {
            var amount = 110m + 10 * i;
            work.Add(Task.Run(async () => (int)(await client.BidAsync(auction, amount)).StatusCode));
            work.Add(Task.Run(async () => (int)(await PostWithKeyAsync(client, "/api/v1/wallet/withdraw", 30m, null)).StatusCode));
        }
        var results = await Task.WhenAll(work);
        await f.IdleAsync();

        // Anything may be refused (a lost bid race, not enough available), but nothing may fail with a server error such as a deadlock.
        Assert.All(results, code => Assert.True(code < 500, $"server error {code}"));
        var balance = await BalanceAsync(id);
        var ledgerSum = await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == id).SumAsync(l => l.Amount));
        var held = await f.DbAsync(db => db.Bids.Where(b => b.UserId == id && b.HeldAmount > 0 && b.ReleasedAtUtc == null && b.SettledAtUtc == null).SumAsync(b => (decimal?)b.HeldAmount)) ?? 0;
        var pendingNow = await f.DbAsync(db => db.WalletTransactions.Where(t => t.UserId == id && t.Type == WalletTransactionType.Withdrawal && t.Status == WalletTransactionStatus.Pending).SumAsync(t => (decimal?)t.Amount)) ?? 0;
        Assert.Equal(balance, ledgerSum);
        Assert.True(balance >= held + pendingNow, $"over-committed: balance {balance}, held {held}, pending {pendingNow}");
        Assert.True(balance >= 0);
    }

    // ---------- idempotency keys ----------

    [Fact]
    public async Task A_retried_request_with_the_same_key_creates_one_request()
    {
        var (id, client, _) = await UserAsync(500);
        var topKey = "k-" + Guid.NewGuid().ToString("N");
        var wdKey = "k-" + Guid.NewGuid().ToString("N");

        await PostWithKeyAsync(client, "/api/v1/wallet/increase", 10m, topKey);
        await PostWithKeyAsync(client, "/api/v1/wallet/increase", 10m, topKey);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostWithKeyAsync(client, "/api/v1/wallet/withdraw", 20m, wdKey)));

        Assert.Single(await f.DbAsync(db => db.WalletTransactions.Where(t => t.UserId == id && t.IdempotencyKey == topKey).ToListAsync()));
        Assert.Single(await f.DbAsync(db => db.WalletTransactions.Where(t => t.UserId == id && t.IdempotencyKey == wdKey).ToListAsync()));

        // The key belongs to one request: a different amount or a different kind of request is a conflict, not a silent return of the old one.
        Assert.Equal(HttpStatusCode.Conflict, (await PostWithKeyAsync(client, "/api/v1/wallet/withdraw", 25m, wdKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostWithKeyAsync(client, "/api/v1/wallet/withdraw", 10m, topKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostWithKeyAsync(client, "/api/v1/wallet/increase", 20m, wdKey)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWithKeyAsync(client, "/api/v1/wallet/withdraw", 20m, new string('x', 101))).StatusCode);
    }

    // ---------- failure and access ----------

    [Fact]
    public async Task A_push_outage_does_not_undo_an_approval()
    {
        var (id, client, _) = await UserAsync(100);
        var tx = await RequestAsync(client, "increase", 15m);
        var admin = await AdminAsync();

        f.Push.Throw = true;
        try
        {
            await WalletAsync(w => w.ApproveAsync(admin, tx));
            await f.IdleAsync();
        }
        finally { f.Push.Throw = false; }

        Assert.Equal(115m, await BalanceAsync(id));
        Assert.Equal(WalletTransactionStatus.Approved, await StatusAsync(tx));
        Assert.Single(await RowsAsync(tx));   // the in-app row is the source of truth
    }

    [Fact]
    public async Task Only_an_administrator_can_list_approve_reject_or_reverse_requests()
    {
        var (_, client, _) = await UserAsync(100);
        var anonymous = f.Client();
        foreach (var call in new Func<ApiClient, Task<HttpResponseMessage>>[]
        {
            c => c.GetAsync("/api/admin/transactions"),
            c => c.PostAsync("/api/admin/transactions/1/approve"),
            c => c.PostAsync("/api/admin/transactions/1/reject"),
            c => c.PostAsync("/api/admin/transactions/1/reverse"),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await call(client)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await call(anonymous)).StatusCode);
        }
    }
}

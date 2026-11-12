using System.Net;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

/// <summary>
/// Phase 6 follow-up decisions: separation of duties on credits, accounts with money cannot be deleted, no decisions for inactive or deleted owners,
/// idempotent admin credits, the opening credit in the same transaction as the new user, and a cap on Pending requests per user.
/// </summary>
[Collection(ApiCollection.Name)]
public class WalletSafeguardsTests(ApiFactory f)
{
    private sealed class FlagScope : IDisposable
    {
        private readonly TestBiddingSettings _flags;
        public FlagScope(TestBiddingSettings flags, bool on) { _flags = flags; flags.On = on; }
        public void Dispose() => _flags.On = false;
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = f.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<T> WalletAsync<T>(Func<WalletService, Task<T>> work) => InScopeAsync(sp => work(sp.GetRequiredService<WalletService>()));

    private async Task<Actor> AdminAsync() => new(await f.DbAsync(db => db.Users.Where(u => u.Email == DevSeed.AdminEmail).Select(u => u.Id).SingleAsync()), UserRole.Admin);

    private async Task<(int id, ApiClient client)> CustomerAsync(decimal wallet = 500, WebApplicationFactory<Program>? host = null)
    {
        var (id, email) = await f.NewCustomerAsync(wallet);
        var client = host is null ? await f.SignedInAsync(email, ApiFactory.Password) : new ApiClient(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }));
        if (host is not null) Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, ApiFactory.Password)).StatusCode);
        return (id, client);
    }

    private Task<List<AuditLog>> AuditAsync(string action, string entityType, int entityId) =>
        f.DbAsync(db => db.AuditLogs.Where(l => l.Action == action && l.EntityType == entityType && l.EntityId == entityId.ToString()).ToListAsync());

    private Task<decimal> BalanceAsync(int userId) => f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync());
    private Task<WalletTransactionStatus> StatusAsync(int id) => f.DbAsync(db => db.WalletTransactions.Where(t => t.Id == id).Select(t => t.Status).SingleAsync());

    private async Task<int> RequestAsync(ApiClient c, string kind, decimal amount, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/wallet/{kind}") { Content = System.Net.Http.Json.JsonContent.Create(new { amount }, options: ApiClient.Json) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        var r = await c.Http.SendAsync(req);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await f.DbAsync(db => db.WalletTransactions.OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync());
    }

    private static async Task<HttpResponseMessage> TryRequestAsync(ApiClient c, string kind, decimal amount, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/wallet/{kind}") { Content = System.Net.Http.Json.JsonContent.Create(new { amount }, options: ApiClient.Json) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return await c.Http.SendAsync(req);
    }

    // ---------- 4. separation of duties on credits ----------

    [Fact]
    public async Task An_administrator_cannot_credit_their_own_wallet_and_the_attempt_is_audited()
    {
        var admin = await AdminAsync();
        var before = await BalanceAsync(admin.UserId);

        var ex = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.CreateApprovedCreditAsync(admin, admin.UserId, 10m, null, null, null)));

        Assert.Equal("wallet.self_credit_not_allowed", ex.Code);
        Assert.Equal(403, ex.Status);
        Assert.Equal(before, await BalanceAsync(admin.UserId));
        Assert.Empty(await f.DbAsync(db => db.WalletTransactions.Where(t => t.UserId == admin.UserId && t.Type == WalletTransactionType.Credit && t.Status == WalletTransactionStatus.Approved).ToListAsync()));
        var row = Assert.Single(await AuditAsync("wallet.credit_refused", nameof(User), admin.UserId), l => l.Data!.Contains("self_credit"));
        Assert.Equal(admin.UserId, row.ActorUserId);

        var (other, _) = await CustomerAsync(0);
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, other, 10m, null, null, null));   // crediting anyone else is unchanged
        Assert.Equal(10m, await BalanceAsync(other));
    }

    // ---------- 6. accounts with money cannot be deleted ----------

    [Fact]
    public async Task An_account_with_a_balance_cannot_be_deleted_and_the_refusal_is_audited()
    {
        var (id, client) = await CustomerAsync(100);

        var r = await client.DeleteAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ACCOUNT_DELETE_BLOCKED", await ApiClient.ErrorCodeAsync(r));
        Assert.False(await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.IsDeleted).SingleAsync()));
        await client.DataAsync(client.GetAsync("/api/v1/profile"));   // still signed in
        var row = Assert.Single(await AuditAsync("user.delete_refused", nameof(User), id));
        Assert.Equal(id, row.ActorUserId);
        Assert.Contains("100", row.Data);
    }

    [Fact]
    public async Task An_account_with_a_pending_withdrawal_cannot_be_deleted_until_it_is_decided()
    {
        var admin = await AdminAsync();
        var (id, client) = await CustomerAsync(100);
        var tx = await RequestAsync(client, "withdraw", 30m);
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 0m)));   // balance gone, request still pending

        var blocked = await client.DeleteAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("ACCOUNT_DELETE_BLOCKED", await ApiClient.ErrorCodeAsync(blocked));
        Assert.Contains("30", Assert.Single(await AuditAsync("user.delete_refused", nameof(User), id)).Data);

        await WalletAsync(w => w.RejectAsync(admin, tx));   // decided: nothing is pending and nothing is left, so the account can go
        await client.DataAsync(client.DeleteAsync("/api/v1/profile"));
        Assert.True(await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.IsDeleted).SingleAsync()));
    }

    [Fact]
    public async Task An_account_with_money_held_by_a_leading_bid_cannot_be_deleted()
    {
        using var _ = new FlagScope(f.Flags, true);
        var (id, client) = await CustomerAsync(200);
        var auction = await f.NewAuctionAsync(start: 100, increment: 10);
        await client.DataAsync(client.BidAsync(auction, 110));
        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, 0m)));   // only the hold is left

        var r = await client.DeleteAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ACCOUNT_DELETE_BLOCKED", await ApiClient.ErrorCodeAsync(r));
        Assert.Contains("110", Assert.Single(await AuditAsync("user.delete_refused", nameof(User), id)).Data);
        Assert.False(await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.IsDeleted).SingleAsync()));
    }

    [Fact]
    public async Task An_account_with_nothing_in_play_can_still_be_deleted()
    {
        var (id, client) = await CustomerAsync(0);
        await client.DataAsync(client.DeleteAsync("/api/v1/profile"));
        Assert.True(await f.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.IsDeleted).SingleAsync()));
    }

    // ---------- 7. no approve/reverse for inactive or deleted owners ----------

    [Fact]
    public async Task Approving_or_reversing_for_an_inactive_or_deleted_owner_is_refused_and_audited_but_rejecting_still_works()
    {
        var admin = await AdminAsync();
        var (id, client) = await CustomerAsync(100);
        var topUp = await RequestAsync(client, "increase", 20m);
        var withdrawal = await RequestAsync(client, "withdraw", 10m);
        var credit = (await WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 15m, null, null, null))).Id;
        var balance = await BalanceAsync(id);

        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        foreach (var tx in new[] { topUp, withdrawal })
        {
            var ex = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ApproveAsync(admin, tx)));
            Assert.Equal("wallet.owner_inactive", ex.Code);
            Assert.Equal(WalletTransactionStatus.Pending, await StatusAsync(tx));
            Assert.Contains("owner_inactive", Assert.Single(await AuditAsync("wallet.approve_refused", nameof(WalletTransaction), tx)).Data);
        }
        var reverse = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ReverseAsync(admin, credit)));
        Assert.Equal("wallet.owner_inactive", reverse.Code);
        Assert.Equal(WalletTransactionStatus.Approved, await StatusAsync(credit));
        Assert.Contains("owner_inactive", Assert.Single(await AuditAsync("wallet.reverse_refused", nameof(WalletTransaction), credit)).Data);
        Assert.Equal(balance, await BalanceAsync(id));

        await WalletAsync(w => w.RejectAsync(admin, withdrawal));   // rejecting moves no money, so it stays possible
        Assert.Equal(WalletTransactionStatus.Rejected, await StatusAsync(withdrawal));

        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true)));
        await WalletAsync(w => w.ApproveAsync(admin, topUp));   // active again: decisions work as before
        Assert.Equal(balance + 20m, await BalanceAsync(id));

        await f.DbAsync(db => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDeleted, true)));
        var deleted = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.ReverseAsync(admin, credit)));
        Assert.Equal("wallet.owner_inactive", deleted.Code);
    }

    // ---------- 8. idempotent admin credit ----------

    [Fact]
    public async Task An_admin_credit_with_a_key_credits_once_even_when_retried_or_submitted_at_once()
    {
        var admin = await AdminAsync();
        var (id, _) = await CustomerAsync(0);
        var key = "credit-" + Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 25m, null, null, null, key))));

        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal(25m, await BalanceAsync(id));
        Assert.Single(await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == id && l.Reason == LedgerReason.AdminCredit).ToListAsync()));
        Assert.Single(await AuditAsync("wallet.credit", nameof(WalletTransaction), results[0].Id));
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 25m, null, null, null, key));   // a later retry: same answer, no second credit
        Assert.Equal(25m, await BalanceAsync(id));

        var other = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 30m, null, null, null, key)));
        Assert.Equal("wallet.idempotency_key_reused", other.Code);
        Assert.Equal(409, other.Status);
        Assert.Equal(25m, await BalanceAsync(id));

        var tooLong = await Assert.ThrowsAsync<AppException>(() => WalletAsync(w => w.CreateApprovedCreditAsync(admin, id, 1m, null, null, null, new string('k', 91))));
        Assert.Equal("wallet.invalid_idempotency_key", tooLong.Code);
    }

    [Fact]
    public async Task Credit_keys_belong_to_one_user_and_credits_without_a_key_are_never_merged()
    {
        var admin = await AdminAsync();
        var (a, _) = await CustomerAsync(0);
        var (b, _) = await CustomerAsync(0);
        var key = "shared-" + Guid.NewGuid().ToString("N");

        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, a, 10m, null, null, null, key));
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, b, 10m, null, null, null, key));   // the same key for another user is a different credit
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, a, 5m, null, null, null));
        await WalletAsync(w => w.CreateApprovedCreditAsync(admin, a, 5m, null, null, null));          // no key: two deliberate credits

        Assert.Equal(20m, await BalanceAsync(a));
        Assert.Equal(10m, await BalanceAsync(b));
    }

    // ---------- 9. opening credit in the same transaction ----------

    [Fact]
    public async Task The_opening_credit_and_the_new_user_are_created_together_or_not_at_all()
    {
        var admin = await AdminAsync();
        var email = $"open{Guid.NewGuid():N}@test.local";
        AccountFieldsInput Input() => new("Opening Test", email, "079" + Random.Shared.Next(1_000_000, 9_999_999), ApiFactory.Password, true, null, null, null, null, null, null);

        f.Failure.Armed = true;   // the ledger write of the credit fails
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => InScopeAsync(async sp => await sp.GetRequiredService<AdminPeople>().CreateUserAsync(admin, Input(), new ProfileFiles(null, null, null, null), 40m)));
        }
        finally { f.Failure.Armed = false; }

        Assert.False(await f.DbAsync(db => db.Users.AnyAsync(u => u.Email == email)));   // no user without the credit
        Assert.Empty(await f.DbAsync(db => db.AuditLogs.Where(l => l.Action == "user.create" && l.Data != null && l.Data.Contains(email)).ToListAsync()));

        var id = await InScopeAsync(sp => sp.GetRequiredService<AdminPeople>().CreateUserAsync(admin, Input(), new ProfileFiles(null, null, null, null), 40m));

        Assert.Equal(40m, await BalanceAsync(id));
        var ledger = Assert.Single(await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == id).ToListAsync()));
        Assert.Equal(LedgerReason.AdminCredit, ledger.Reason);
        Assert.Single(await AuditAsync("user.create", nameof(User), id));
        var credit = await f.DbAsync(db => db.WalletTransactions.SingleAsync(t => t.UserId == id));
        Assert.Equal(WalletTransactionStatus.Approved, credit.Status);
        Assert.Equal(admin.UserId, Assert.Single(await AuditAsync("wallet.credit", nameof(WalletTransaction), credit.Id)).ActorUserId);
    }

    // ---------- 10. cap on Pending requests ----------

    [Fact]
    public async Task A_user_cannot_have_more_than_the_configured_number_of_pending_requests()
    {
        using var host = f.WithWebHostBuilder(b => b.UseSetting("Wallet:MaxPendingRequests", "3"));
        var (id, client) = await CustomerAsync(500, host);
        var admin = await AdminAsync();
        var first = await RequestAsync(client, "increase", 10m, "k1");
        await RequestAsync(client, "increase", 11m, "k2");
        await RequestAsync(client, "withdraw", 5m);

        var topUp = await TryRequestAsync(client, "increase", 12m);
        var withdrawal = await TryRequestAsync(client, "withdraw", 5m);
        Assert.Equal(HttpStatusCode.Conflict, topUp.StatusCode);
        Assert.Equal("WALLET_TOO_MANY_PENDING", await ApiClient.ErrorCodeAsync(topUp));
        Assert.Equal(HttpStatusCode.Conflict, withdrawal.StatusCode);
        Assert.Equal(3, await f.DbAsync(db => db.WalletTransactions.CountAsync(t => t.UserId == id && t.Status == WalletTransactionStatus.Pending)));

        Assert.Equal(HttpStatusCode.OK, (await TryRequestAsync(client, "increase", 10m, "k1")).StatusCode);   // a retry of an existing request still answers at the cap
        Assert.Equal(3, await f.DbAsync(db => db.WalletTransactions.CountAsync(t => t.UserId == id)));

        await WalletAsync(w => w.RejectAsync(admin, first));
        Assert.Equal(HttpStatusCode.OK, (await TryRequestAsync(client, "increase", 12m)).StatusCode);          // room again once one is decided
    }

    [Fact]
    public async Task Requests_made_at_the_same_moment_cannot_get_past_the_cap()
    {
        using var host = f.WithWebHostBuilder(b => b.UseSetting("Wallet:MaxPendingRequests", "3"));
        var (id, client) = await CustomerAsync(500, host);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => TryRequestAsync(client, i % 2 == 0 ? "increase" : "withdraw", 1m + i)));

        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(3, await f.DbAsync(db => db.WalletTransactions.CountAsync(t => t.UserId == id && t.Status == WalletTransactionStatus.Pending)));
    }
}

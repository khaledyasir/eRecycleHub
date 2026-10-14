using System.Net;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WalletTests(ApiFactory f)
{
    private async Task<decimal> BalanceAsync(int userId) => await f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync());
    private async Task<decimal> LedgerSumAsync(int userId) => await f.DbAsync(async db => await db.WalletLedger.Where(l => l.UserId == userId).SumAsync(l => (decimal?)l.Amount) ?? 0);

    private static async Task<int> TopUpAsync(ApiClient c, decimal amount, string? key = null)
    {
        var r = await c.MultipartAsync(HttpMethod.Post, "/api/account/wallet/top-ups", new { amount, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(ApiClient.Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Top_up_request_is_pending_owned_by_caller_and_does_not_change_balance()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 500);
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        // Extra fields a client might try to smuggle (userId, status) are simply not part of the contract.
        var r = await c.MultipartAsync(HttpMethod.Post, "/api/account/wallet/top-ups", new { amount = 25m, userId = 1, status = "Approved" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = await f.DbAsync(db => db.WalletTransactions.Where(x => x.UserId == id).SingleAsync());
        Assert.Equal(WalletTransactionStatus.Pending, t.Status);
        Assert.Equal(500m, await BalanceAsync(id));
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(10.123)]
    [InlineData(1000000.01)]
    public async Task Invalid_top_up_amounts_create_nothing(decimal amount)
    {
        var (id, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var r = await c.MultipartAsync(HttpMethod.Post, "/api/account/wallet/top-ups", new { amount });
        Assert.Equal("wallet.invalid_amount", await ApiClient.CodeAsync(r));
        Assert.Equal(0, await f.DbAsync(db => db.WalletTransactions.CountAsync(x => x.UserId == id)));
    }

    [Fact]
    public async Task Retried_top_up_with_same_idempotency_key_creates_one_request_even_concurrently()
    {
        var (id, email) = await f.NewCustomerAsync();
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        await c.CsrfAsync();
        var key = Guid.NewGuid().ToString();
        var ids = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TopUpAsync(c, 40, key)));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await f.DbAsync(db => db.WalletTransactions.CountAsync(x => x.UserId == id)));
    }

    [Fact]
    public async Task Approve_credits_exactly_once_even_when_repeated_concurrently()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 500);
        var tx = await TopUpAsync(await f.SignedInAsync(email, ApiFactory.Password), 25);
        var admins = new List<ApiClient>();
        for (var i = 0; i < 4; i++) { var a = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword); await a.CsrfAsync(); admins.Add(a); }

        var results = await Task.WhenAll(admins.Select(a => a.PostAsync($"/api/admin/transactions/{tx}/approve")));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(525m, await BalanceAsync(id));
        Assert.Equal(525m, await LedgerSumAsync(id));
        Assert.Equal(1, await f.DbAsync(db => db.WalletLedger.CountAsync(l => l.WalletTransactionId == tx)));
        Assert.Equal(1, await f.DbAsync(db => db.Notifications.CountAsync(n => n.UserId == id && n.Kind == NotificationKinds.TopUpApproved)));
    }

    [Fact]
    public async Task Rejected_request_never_credits_and_cannot_be_approved_later()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 0);
        var tx = await TopUpAsync(await f.SignedInAsync(email, ApiFactory.Password), 60);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/transactions/{tx}/reject")).StatusCode);
        Assert.Equal("wallet.not_pending", await ApiClient.CodeAsync(await admin.PostAsync($"/api/admin/transactions/{tx}/approve")));
        Assert.Equal(0m, await BalanceAsync(id));
    }

    [Fact]
    public async Task Reversal_debits_once_and_approved_credits_cannot_be_deleted()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 100);
        var tx = await TopUpAsync(await f.SignedInAsync(email, ApiFactory.Password), 30);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        await admin.PostAsync($"/api/admin/transactions/{tx}/approve");
        Assert.Equal("wallet.delete_not_allowed", await ApiClient.CodeAsync(await admin.DeleteAsync($"/api/admin/transactions/{tx}")));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/transactions/{tx}/reverse")).StatusCode);
        Assert.Equal("wallet.not_approved", await ApiClient.CodeAsync(await admin.PostAsync($"/api/admin/transactions/{tx}/reverse")));
        Assert.Equal(100m, await BalanceAsync(id));
        Assert.Equal(100m, await LedgerSumAsync(id));
    }

    [Fact]
    public async Task Database_failure_during_approval_rolls_back_status_and_balance()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 10);
        var tx = await TopUpAsync(await f.SignedInAsync(email, ApiFactory.Password), 15);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        await admin.CsrfAsync();

        f.Failure.Armed = true;
        var failed = await admin.PostAsync($"/api/admin/transactions/{tx}/approve");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(WalletTransactionStatus.Pending, await f.DbAsync(db => db.WalletTransactions.Where(t => t.Id == tx).Select(t => t.Status).SingleAsync()));
        Assert.Equal(10m, await BalanceAsync(id));

        // The retry after the failure succeeds and credits exactly once.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/transactions/{tx}/approve")).StatusCode);
        Assert.Equal(25m, await BalanceAsync(id));
        Assert.Equal(25m, await LedgerSumAsync(id));
    }

    [Fact]
    public async Task Admin_direct_credit_is_approved_and_ledgered()
    {
        var (id, _) = await f.NewCustomerAsync(wallet: 0);
        var admin = await f.SignedInAsync(DevSeed.AdminEmail, DevSeed.AdminPassword);
        var r = await admin.MultipartAsync(HttpMethod.Post, "/api/admin/transactions", new { userId = id, amount = 12.5m, note = "manual" }, ("proof", "proof.pdf", TestFiles.Pdf));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(12.5m, await BalanceAsync(id));
        var proofOwner = await f.DbAsync(db => db.WalletTransactions.Where(t => t.UserId == id).Select(t => db.Files.Where(x => x.Id == t.ProofFileId).Select(x => x.OwnerUserId).Single()).SingleAsync());
        Assert.Equal(id, proofOwner);
    }

    [Fact]
    public async Task Customer_cannot_use_admin_wallet_endpoints()
    {
        var (id, email) = await f.NewCustomerAsync(wallet: 0);
        var c = await f.SignedInAsync(email, ApiFactory.Password);
        var r = await c.MultipartAsync(HttpMethod.Post, "/api/admin/transactions", new { userId = id, amount = 99999m });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0m, await BalanceAsync(id));
    }

    [Fact]
    public async Task Balance_always_equals_ledger_sum_for_seeded_accounts()
    {
        var mismatches = await f.DbAsync(db => db.Users.Where(u => u.WalletBalance != (db.WalletLedger.Where(l => l.UserId == u.Id).Sum(l => (decimal?)l.Amount) ?? 0))
            .Where(u => u.Email.EndsWith("@test.local") && !u.Email.StartsWith("u")).CountAsync());
        Assert.Equal(0, mismatches);
    }
}

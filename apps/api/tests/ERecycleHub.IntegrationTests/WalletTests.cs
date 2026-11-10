using Microsoft.Extensions.DependencyInjection;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>Phase 1: the wallet's debit/credit core, the balance and ledger endpoints.</summary>
[Collection(ApiCollection.Name)]
public class WalletTests(ApiFactory f)
{
    /// <summary>Runs one debit or credit the way a caller (e.g. bidding) does: in its own transaction, saved and committed.</summary>
    private async Task<WalletPosting> PostAsync(int userId, decimal amount, bool debit = true, bool commit = true)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletService>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var posting = debit
            ? await wallet.DebitAsync(userId, amount, LedgerReason.BidDeduction, LedgerReferences.Bid, 7)
            : await wallet.CreditAsync(userId, amount, LedgerReason.BidRefund, LedgerReferences.Bid, 7);
        await db.SaveChangesAsync();
        if (commit) await tx.CommitAsync();
        return posting;
    }

    private Task<decimal> BalanceOfAsync(int userId) => f.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).SingleAsync());

    [Fact]
    public async Task Debit_lowers_the_balance_and_records_before_after_and_reference()
    {
        var (id, _) = await f.NewCustomerAsync(500);
        var posting = await PostAsync(id, 120.50m);

        Assert.Equal(500m, posting.Before);
        Assert.Equal(379.50m, posting.After);
        Assert.Equal(379.50m, await BalanceOfAsync(id));
        var entry = await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == id && l.Reason == LedgerReason.BidDeduction).SingleAsync());
        Assert.Equal(-120.50m, entry.Amount);
        Assert.Equal(500m, entry.BalanceBefore);
        Assert.Equal(379.50m, entry.BalanceAfter);
        Assert.Equal(LedgerReferences.Bid, entry.ReferenceType);
        Assert.Equal(7, entry.ReferenceId);
    }

    [Fact]
    public async Task Credit_raises_the_balance()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        var posting = await PostAsync(id, 40m, debit: false);
        Assert.Equal(100m, posting.Before);
        Assert.Equal(140m, posting.After);
        Assert.Equal(140m, await BalanceOfAsync(id));
    }

    [Fact]
    public async Task Debit_beyond_the_balance_is_refused_and_changes_nothing()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        var ex = await Assert.ThrowsAsync<AppException>(() => PostAsync(id, 100.01m));
        Assert.Equal("wallet.insufficient", ex.Code);
        Assert.Equal(422, ex.Status);
        Assert.Equal(100m, await BalanceOfAsync(id));
        Assert.Equal(0, await f.DbAsync(db => db.WalletLedger.CountAsync(l => l.UserId == id && l.Reason == LedgerReason.BidDeduction)));
    }

    [Fact]
    public async Task Debiting_the_exact_balance_leaves_zero_never_negative()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        await PostAsync(id, 100m);
        Assert.Equal(0m, await BalanceOfAsync(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    public async Task Invalid_amounts_are_refused(double amount)
    {
        var (id, _) = await f.NewCustomerAsync(100);
        var ex = await Assert.ThrowsAsync<AppException>(() => PostAsync(id, (decimal)amount));
        Assert.Equal("wallet.invalid_amount", ex.Code);
        Assert.Equal(100m, await BalanceOfAsync(id));
    }

    [Fact]
    public async Task Debit_outside_a_transaction_is_refused()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        using var scope = f.Services.CreateScope();
        var wallet = scope.ServiceProvider.GetRequiredService<WalletService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => wallet.DebitAsync(id, 10m, LedgerReason.BidDeduction, null, null));
        Assert.Equal(100m, await BalanceOfAsync(id));
    }

    [Fact]
    public async Task Rolled_back_transaction_leaves_balance_and_ledger_untouched()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        await PostAsync(id, 30m, commit: false);
        Assert.Equal(100m, await BalanceOfAsync(id));
        Assert.Equal(0, await f.DbAsync(db => db.WalletLedger.CountAsync(l => l.UserId == id && l.Reason == LedgerReason.BidDeduction)));
    }

    [Fact]
    public async Task Concurrent_debits_never_overdraw()
    {
        var (id, _) = await f.NewCustomerAsync(100);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try { await PostAsync(id, 30m); return true; }
            catch (AppException ex) when (ex.Code == "wallet.insufficient") { return false; }
        }));

        Assert.Equal(3, results.Count(ok => ok));
        Assert.Equal(10m, await BalanceOfAsync(id));
        var ledgerSum = await f.DbAsync(db => db.WalletLedger.Where(l => l.UserId == id).SumAsync(l => l.Amount));
        Assert.Equal(10m, ledgerSum);
    }

    [Fact]
    public async Task Balance_endpoint_subtracts_pending_withdrawals()
    {
        var c = await f.SignedInAsync((await f.NewCustomerAsync(100)).email, ApiFactory.Password);
        await c.DataAsync(c.PostAsync("/api/v1/wallet/withdraw", new { amount = 40 }));

        var data = await c.DataAsync(c.GetAsync("/api/v1/wallet/balance"));
        Assert.Equal(100m, data.GetProperty("balance").GetDecimal());
        Assert.Equal(40m, data.GetProperty("pendingWithdrawals").GetDecimal());
        Assert.Equal(60m, data.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task Ledger_endpoint_lists_newest_first_and_paginates()
    {
        var (id, email) = await f.NewCustomerAsync(100);
        await PostAsync(id, 25m);
        var c = await f.SignedInAsync(email, ApiFactory.Password);

        var all = await c.DataAsync(c.GetAsync("/api/v1/wallet/ledger"));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var first = all.GetProperty("items")[0];
        Assert.Equal("BidDeduction", first.GetProperty("reason").GetString());
        Assert.Equal(-25m, first.GetProperty("amount").GetDecimal());
        Assert.Equal(100m, first.GetProperty("balanceBefore").GetDecimal());
        Assert.Equal(75m, first.GetProperty("balanceAfter").GetDecimal());
        Assert.Equal("bid", first.GetProperty("referenceType").GetString());
        var legacy = all.GetProperty("items")[1];
        Assert.Equal("MigrationOpeningBalance", legacy.GetProperty("reason").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, legacy.GetProperty("balanceBefore").ValueKind);

        var page2 = await c.DataAsync(c.GetAsync("/api/v1/wallet/ledger?page=2&pageSize=1"));
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.Equal("MigrationOpeningBalance", page2.GetProperty("items")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Ledger_shows_only_the_callers_entries_and_needs_a_token()
    {
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await f.Client().GetAsync("/api/v1/wallet/ledger")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await f.Client().GetAsync("/api/v1/wallet/balance")).StatusCode);

        var (otherId, _) = await f.NewCustomerAsync(500);
        var c = await f.SignedInAsync((await f.NewCustomerAsync(10)).email, ApiFactory.Password);
        var data = await c.DataAsync(c.GetAsync("/api/v1/wallet/ledger"));
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.NotEqual(0, otherId);
    }
}

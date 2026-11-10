using ERecycleHub.Application;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api;

/// <summary>
/// Maintenance commands: <c>dotnet run -- migrate</c> applies EF Core migrations; <c>dotnet run -- check-wallets</c> checks that balances, held bid money and the ledger agree (read-only, exit code 1 on mismatch); <c>dotnet run -- resync-topics</c> rebuilds every push-topic subscription from the database; <c>dotnet run -- seed-dev</c> applies migrations and
/// loads development data (Development environment only, empty database only). Returns false for unknown arguments so the web host starts.
/// </summary>
public static class Commands
{
    public static async Task<bool> RunAsync(WebApplication app, string[] args)
    {
        var command = args[0];
        if (command is not ("migrate" or "seed-dev" or "resync-topics" or "check-wallets")) return false;
        using var scope = app.Services.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Commands");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (command == "check-wallets")
        {
            // Read-only. Exit code 1 when the books do not add up (errors); warnings are printed but do not fail it.
            var r = await scope.ServiceProvider.GetRequiredService<WalletConsistency>().CheckAsync();
            Console.WriteLine($"Users checked: {r.UsersChecked}. Total balances {r.TotalBalances:0.00}, total ledger {r.TotalLedger:0.00}.");
            Console.WriteLine($"Auctions with active holds: {r.AuctionsWithHolds}. Total held on bids {r.TotalHeld:0.00} (reserved, not debited).");
            Console.WriteLine($"Auction payments: {r.PaymentsChecked}. Platform revenue (sum of AuctionPayment) {r.PlatformRevenue:0.00}.");
            foreach (var m in r.UserMismatches) Console.WriteLine($"ERROR USER {m.UserId}: balance {m.WalletBalance:0.00} but ledger {m.LedgerSum:0.00} (difference {m.Difference:0.00})");
            foreach (var m in r.OverCommitted) Console.WriteLine($"ERROR USER {m.UserId}: balance {m.Balance:0.00} < pending withdrawals {m.Pending:0.00} + held bids {m.Held:0.00} (short by {m.Shortfall:0.00})");
            foreach (var m in r.AuctionMismatches) Console.WriteLine($"ERROR AUCTION {m.AuctionId}: held {m.HeldOnBids:0.00}, holding bids {m.HoldingBids}: {m.Problem}");
            foreach (var m in r.SettlementMismatches) Console.WriteLine($"ERROR BID {m.BidId} (auction {m.AuctionId}): {m.Problem}");
            foreach (var u in r.UnsettledWarnings)
                Console.WriteLine($"WARNING AUCTION {u.AuctionId} ({u.RefNumber}): ended {u.EndedAtUtc:u}, winner {u.WinnerName} <{u.WinnerEmail}> holds {u.Amount:0.00} but is not charged yet"
                    + (u.SettlementFailedAtUtc is null ? " (waiting for the next close run)" : $"; payment FAILED at {u.SettlementFailedAtUtc:u}, wallet {u.WinnerBalance:0.00}, short by {u.Shortfall:0.00}"));
            if (r.LegacyHoldLedgerRows > 0) Console.WriteLine($"WARNING: {r.LegacyHoldLedgerRows} ledger row(s) from the old debit-at-bid model are present (expected only if the flag was ever on before the hold-only change).");
            if (r.Truncated) Console.WriteLine("(list truncated)");
            Console.WriteLine(r.Ok ? "OK: the wallet books are consistent." : "MISMATCH: see the ERROR lines above.");
            if (!r.Ok) Environment.ExitCode = 1;
            return true;
        }

        if (command == "resync-topics")
        {
            var handled = await scope.ServiceProvider.GetRequiredService<AuctionTopicService>().ResyncAllAsync();
            log.LogInformation("Push topic subscriptions rebuilt for {Count} running auction(s).", handled);
            return true;
        }

        log.LogInformation("Applying migrations to {Database}", db.Database.GetDbConnection().Database);
        await db.Database.MigrateAsync();

        if (command == "seed-dev")
        {
            if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("seed-dev creates well-known accounts and only runs in the Development or Testing environment.");
            if (await db.Users.AnyAsync())
            {
                log.LogInformation("Database already has users; seed skipped.");
                return true;
            }
            var seed = ActivatorUtilities.CreateInstance<DevSeed>(scope.ServiceProvider);
            await seed.RunAsync();
            log.LogInformation("Development data loaded.");
        }
        return true;
    }
}

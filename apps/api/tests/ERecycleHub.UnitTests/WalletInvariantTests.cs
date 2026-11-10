using System.Text.RegularExpressions;

namespace ERecycleHub.UnitTests;

/// <summary>Architecture guard: the wallet balance has exactly one writer, so the ledger, the guard and the locks cannot be bypassed by accident.</summary>
public class WalletInvariantTests
{
    /// <summary>The service that owns every balance change, the one-time migration script that converts old held bids, and the development seed.</summary>
    private static readonly string[] Allowed = ["WalletService.cs", "HoldModelConversion.cs", "DevSeed.cs"];

    private static readonly Regex[] Writes =
    [
        new(@"\bWalletBalance\s*(\+=|-=|=(?!=))"),               // assignment or compound assignment
        new(@"SetProperty\(\s*\w+\s*=>\s*\w+\.WalletBalance"),    // ExecuteUpdate
        new(@"SET\s+(\w+\.)?WalletBalance", RegexOptions.IgnoreCase),   // raw SQL
    ];

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ERecycleHub.Application"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("apps/api/src not found") : Path.Combine(dir.FullName, "src");
    }

    [Fact]
    public void Only_the_wallet_service_the_one_time_conversion_and_the_dev_seed_write_the_wallet_balance()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (Allowed.Contains(Path.GetFileName(file))) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Writes.Any(r => r.IsMatch(lines[i]))) offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0, "These write WalletBalance outside the wallet service:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_guard_scan_would_catch_a_write()
    {
        Assert.Contains(Writes, r => r.IsMatch("user.WalletBalance += 5;"));
        Assert.Contains(Writes, r => r.IsMatch("s.SetProperty(u => u.WalletBalance, 0m)"));
        Assert.Contains(Writes, r => r.IsMatch("UPDATE Users SET WalletBalance = 0"));
        Assert.DoesNotContain(Writes, r => r.IsMatch("if (u.WalletBalance == 5)"));
        Assert.DoesNotContain(Writes, r => r.IsMatch("decimal WalletBalance,"));
    }
}

using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using ERecycleHub.LegacyImport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>
/// Builds a throw-away LEGACY database from the legacy schema + dev seed (LegacyFixtures/), adds edge-case rows,
/// runs the importer into a throw-away target, and checks the conversions documented in docs/DATABASE-MAPPING.md.
/// </summary>
public sealed class LegacyImportTests : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private string LegacyDb => $"ERH_LegacyTest_{_suffix}";
    private string TargetDb => $"ERH_ImportTest_{_suffix}";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erh-import-" + Guid.NewGuid().ToString("N"));
    private static string Conn(string db) => $"Server=localhost;Database={db};Integrated Security=True;TrustServerCertificate=True";

    /// <summary>Copies of the legacy repository's Database/schema.sql and seed.dev.sql, kept here so the legacy project itself is not needed.</summary>
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "LegacyFixtures");

    private static async Task ExecAsync(string conn, string sql)
    {
        await using var c = new SqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await ExecAsync(Conn("master"), $"CREATE DATABASE [{LegacyDb}]");
        await ExecAsync(Conn(LegacyDb), await File.ReadAllTextAsync(Path.Combine(Fixtures, "schema.sql")));
        await ExecAsync(Conn(LegacyDb), await File.ReadAllTextAsync(Path.Combine(Fixtures, "seed.dev.sql")));

        // Legacy uploads folder with one real image and one KYC document.
        Directory.CreateDirectory(Path.Combine(_root, "App_File", "Auctions"));
        Directory.CreateDirectory(Path.Combine(_root, "App_File", "Publishers"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "App_File", "Auctions", "lot.png"), TestFiles.Png);
        await File.WriteAllBytesAsync(Path.Combine(_root, "App_File", "Publishers", "id.pdf"), TestFiles.Pdf);

        await ExecAsync(Conn(LegacyDb), """
            DECLARE @c int = (SELECT TOP 1 Id FROM [User] WHERE Email='customer@test.local');
            DECLARE @a int = (SELECT TOP 1 Id FROM Auction WHERE RefNumber='T-1');
            -- float amounts that are not cent-exact, and Central-time dates in winter (UTC-6)
            INSERT AuctionBid (AuctionId, UserId, Amount, CreatedDate) VALUES (@a, @c, 110.005, '2026-01-15 10:00:00');
            UPDATE Auction SET CurrentPrice = 110.005, MainImage = 'http://legacy.example/App_File/Auctions/lot.png', StartDate = '2026-01-15 09:00:00', EndDate = '2099-01-01 00:00:00' WHERE Id = @a;
            UPDATE [User] SET IdNumber = 'http://legacy.example/App_File/Publishers/id.pdf', Wallet = 500.1 WHERE Id = @c;
            -- one approved credit that does not explain the balance (reconciliation note), one with a weird status
            INSERT [Transaction] (UserId, Type, Amount, Status, CreatedDate) VALUES (@c, 'Credit', 25, 'Approved', '2026-02-01 08:00:00'), (@c, 'Credit', 5, 'Weird', '2026-02-02 08:00:00');
            -- duplicate e-mail (legacy had no unique index) and a missing file reference
            INSERT [User] (CountryId, UserName, FullName, Email, Phone, Password, RoleId, IsActive, IsDeleted, Wallet, Image)
              VALUES ((SELECT TOP 1 Id FROM Country), '', 'Dup', 'CUSTOMER@test.local', '9', 'x', 3, 1, 0, 0, 'http://legacy.example/App_File/Users/missing.png');
            SET IDENTITY_INSERT Setting ON;
            INSERT Setting (Id, TitleEn, TitleAr, ValueEn, ValueAr) VALUES (3, 'Privacy', N'الخصوصية', '<p>p</p><script>x</script>', N'<p>خ</p>'), (5, 'Terms', N'الشروط', '<p>t</p>', N'<p>ش</p>');
            SET IDENTITY_INSERT Setting OFF;
            """);
    }

    public async Task DisposeAsync()
    {
        foreach (var db in new[] { LegacyDb, TargetDb })
            await ExecAsync(Conn("master"), $"IF DB_ID('{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Imports_legacy_data_with_documented_conversions()
    {
        var storage = Path.Combine(_root, "storage");
        var result = await new LegacyImporter(new ImportOptions(Conn(LegacyDb), Conn(TargetDb), Path.Combine(_root, "App_File"), storage, Path.Combine(_root, "report"), DryRun: false)).RunAsync();
        Assert.True(File.Exists(result.ReportPath));

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn(TargetDb)).Options);
        var pw = new PasswordService();

        // Users: ids kept, plaintext hashed at import (and still valid), duplicate e-mail neutralised.
        var customer = await db.Users.SingleAsync(u => u.Email == "customer@test.local");
        Assert.False(PasswordService.IsLegacyV1(customer.PasswordHash!));
        Assert.DoesNotContain("CustomerPass", customer.PasswordHash);
        Assert.True(pw.Verify(customer.PasswordHash, "CustomerPass#2026", out _));
        Assert.Equal(500.10m, customer.WalletBalance);
        Assert.Equal(500.10m, await db.WalletLedger.Where(l => l.UserId == customer.Id).SumAsync(l => l.Amount));
        var dup = await db.Users.SingleAsync(u => u.FullName == "Dup");
        Assert.EndsWith("@invalid.local", dup.Email);
        Assert.False(dup.IsActive);

        // Money: float → decimal(18,2), rounded half away from zero and reported.
        var bid = await db.Bids.SingleAsync(b => b.UserId == customer.Id);
        Assert.Equal(110.01m, bid.Amount);
        Assert.Contains("110.01", await File.ReadAllTextAsync(Path.Combine(_root, "report", "money-adjustments.csv")));

        // Dates: Central wall-clock → UTC (January = UTC-6).
        Assert.Equal(new DateTime(2026, 1, 15, 16, 0, 0, DateTimeKind.Utc), bid.CreatedAtUtc);
        var auction = await db.Auctions.SingleAsync(a => a.RefNumber == "T-1");
        Assert.Equal(new DateTime(2026, 1, 15, 15, 0, 0, DateTimeKind.Utc), auction.StartsAtUtc);
        Assert.Equal(110.01m, auction.CurrentPrice);
        Assert.Equal(1, auction.BidCount);
        Assert.Null(auction.CategoryId); // no relation is invented for legacy auctions

        // Files: copied + registered; KYC private, auction image public; missing file reported.
        var image = await db.Files.SingleAsync(f => f.Id == auction.MainImageFileId);
        Assert.True(image.IsPublic);
        Assert.True(File.Exists(Path.Combine(storage, image.StoragePath)));
        var idDoc = await db.Files.SingleAsync(f => f.Id == customer.IdDocumentFileId);
        Assert.False(idDoc.IsPublic);
        Assert.Equal(customer.Id, idDoc.OwnerUserId);
        Assert.Contains(result.Notes, n => n.Contains("missing.png"));

        // Wallet history: statuses mapped, unknown → Pending (noted), reconciliation difference listed.
        Assert.Equal(2, await db.WalletTransactions.CountAsync(t => t.UserId == customer.Id));
        Assert.Contains(result.Notes, n => n.Contains("unknown status 'weird'"));
        Assert.Contains(result.Notes, n => n.StartsWith($"Wallet review: user {customer.Id}"));

        // Settings 3/5 become the public privacy/terms pages, sanitised.
        var privacy = await db.Settings.SingleAsync(s => s.Key == SettingKeys.PrivacyPolicy);
        Assert.DoesNotContain("script", privacy.ValueEn);
        Assert.True(await db.Settings.AnyAsync(s => s.Key == SettingKeys.Terms));

        // Legacy v1 hashes that were in the source survive untouched (they upgrade at next login).
        Assert.True(await db.Users.CountAsync(u => u.PasswordHash != null) >= 5);
    }

    [Fact]
    public async Task Refuses_a_non_empty_target_and_dry_run_writes_nothing()
    {
        var opts = new ImportOptions(Conn(LegacyDb), Conn(TargetDb), null, Path.Combine(_root, "s2"), Path.Combine(_root, "r2"), DryRun: true);
        await new LegacyImporter(opts).RunAsync();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Conn(TargetDb)).Options))
            Assert.False(await db.Users.AnyAsync());

        await new LegacyImporter(opts with { DryRun = false }).RunAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LegacyImporter(opts with { DryRun = false }).RunAsync());
    }
}

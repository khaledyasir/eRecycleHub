using ERecycleHub.Application;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api;

/// <summary>
/// Maintenance commands: <c>dotnet run -- migrate</c> applies EF Core migrations; <c>dotnet run -- seed-dev</c> applies migrations and
/// loads development data (Development environment only, empty database only). Returns false for unknown arguments so the web host starts.
/// </summary>
public static class Commands
{
    public static async Task<bool> RunAsync(WebApplication app, string[] args)
    {
        var command = args[0];
        if (command is not ("migrate" or "seed-dev")) return false;
        using var scope = app.Services.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Commands");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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

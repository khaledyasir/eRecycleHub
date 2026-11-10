using ERecycleHub.Application;

namespace ERecycleHub.Api;

/// <summary>
/// Runs <see cref="BackgroundWorkQueue"/> items one at a time, each in its own DI scope and with its own time limit. On shutdown the
/// queue stops accepting work and what is already queued is run before the host exits (within the host's shutdown timeout).
/// </summary>
public sealed class BackgroundWorkRunner(BackgroundWorkQueue queue, IServiceScopeFactory scopes, ILogger<BackgroundWorkRunner> log) : BackgroundService
{
    private static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reads until the queue is completed (by StopAsync) and empty: the stop token must not cut the drain short.
        await foreach (var item in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                using var timeout = new CancellationTokenSource(ItemTimeout);
                using var scope = scopes.CreateScope();
                await item.Work(scope.ServiceProvider, timeout.Token);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Background work failed: {Work}", item.Name);
            }
            finally
            {
                queue.MarkFinished();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}

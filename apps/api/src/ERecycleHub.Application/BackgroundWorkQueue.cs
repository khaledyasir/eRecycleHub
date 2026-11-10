using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

public delegate Task BackgroundWork(IServiceProvider services, CancellationToken ct);

public sealed record BackgroundWorkItem(string Name, BackgroundWork Work);

/// <summary>
/// In-process, bounded work queue for things that must not delay a request or break it when they fail (push subscriptions, later
/// notifications). Work is lost if the process dies or the queue is full, so every job must be safe to rebuild from the database.
/// A full or closed queue drops the item and says so in the log; it never blocks the caller.
/// </summary>
public sealed class BackgroundWorkQueue
{
    private readonly Channel<BackgroundWorkItem> _channel;
    private readonly ILogger<BackgroundWorkQueue> _log;
    private long _enqueued;
    private long _finished;
    private long _dropped;

    public const int DefaultCapacity = 1000;

    public BackgroundWorkQueue(ILogger<BackgroundWorkQueue> log, int capacity = DefaultCapacity)
    {
        _log = log;
        _channel = Channel.CreateBounded<BackgroundWorkItem>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then returns false instead of overwriting queued work
        });
    }

    public ChannelReader<BackgroundWorkItem> Reader => _channel.Reader;

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Queues the work. Returns false (and logs the dropped item's name) when the queue is full or already shut down.</summary>
    public bool TryEnqueue(string name, BackgroundWork work)
    {
        Interlocked.Increment(ref _enqueued);
        if (_channel.Writer.TryWrite(new BackgroundWorkItem(name, work))) return true;
        Interlocked.Decrement(ref _enqueued);
        Interlocked.Increment(ref _dropped);
        _log.LogWarning("Background work dropped (queue full or stopping): {Work}. It will be rebuilt from the database on the next sync.", name);
        return false;
    }

    /// <summary>Stops accepting work. Items already queued can still be read and run.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    public void MarkFinished() => Interlocked.Increment(ref _finished);

    /// <summary>Completes when everything queued so far has run (tests and shutdown checks).</summary>
    public async Task<bool> WhenIdleAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Interlocked.Read(ref _finished) < Interlocked.Read(ref _enqueued))
        {
            if (DateTime.UtcNow > until) return false;
            await Task.Delay(10);
        }
        return true;
    }
}

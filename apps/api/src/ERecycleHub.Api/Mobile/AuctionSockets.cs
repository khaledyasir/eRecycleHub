using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api.Mobile;

/// <summary>
/// Live auction rooms for <c>wss://host/api/ws/auctions/{id}</c> (§3.4.4). Server → client only: JSON text frames
/// <c>{ "event": "...", "data": { ... } }</c>. In-memory, so this works for a single API instance; scale-out needs a backplane.
/// </summary>
public sealed class AuctionSockets(ILogger<AuctionSockets> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Connection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Connection>> _rooms = new();

    /// <summary>Auctions that currently have at least one listener.</summary>
    public IReadOnlyList<int> WatchedAuctions => _rooms.Where(r => !r.Value.IsEmpty).Select(r => r.Key).ToList();

    /// <summary>Holds the connection open until the client closes it or the server stops. Anything the client sends is ignored.</summary>
    public async Task RunAsync(int auctionId, WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var room = _rooms.GetOrAdd(auctionId, _ => new());
        room[id] = new Connection(socket);
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await socket.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Client vanished or the server is stopping.
        }
        finally
        {
            room.TryRemove(id, out _);
            if (room.IsEmpty) _rooms.TryRemove(new KeyValuePair<int, ConcurrentDictionary<Guid, Connection>>(auctionId, room));
        }
    }

    /// <summary>Sends one event to every listener of the auction. Never throws: a failed listener is dropped (it reconnects and reloads).</summary>
    public async Task BroadcastAsync(int auctionId, string eventName, object data)
    {
        if (!_rooms.TryGetValue(auctionId, out var room) || room.IsEmpty) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { @event = eventName, data }, Json);
        await Task.WhenAll(room.Select(async pair =>
        {
            var c = pair.Value;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await c.SendLock.WaitAsync(timeout.Token);
                try { await c.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token); }
                finally { c.SendLock.Release(); }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                log.LogDebug("Dropping auction {AuctionId} listener: {Error}", auctionId, ex.Message);
                room.TryRemove(pair.Key, out _);
                c.Socket.Abort();
            }
        }));
    }

    /// <summary>
    /// Served at both <c>/api/ws/auctions/{id}</c> (what the app uses: everything under /api goes to this API, so the proxy needs
    /// one rule) and the original <c>/ws/auctions/{id}</c>.
    /// </summary>
    public static void Map(WebApplication app)
    {
        foreach (var path in (string[])["/api/ws/auctions/{id:int}", "/ws/auctions/{id:int}"])
            app.Map(path, Handle).RequireAuthorization().WithTags("Auctions").ExcludeFromDescription();
    }

    private static async Task Handle(int id, HttpContext ctx, AuctionQueries queries, AuctionSockets sockets, IHostApplicationLifetime life)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            await ErrorCodes.WriteAsync(ctx, 400, "VALIDATION_FAILED", "request.websocket_required");
            return;
        }
        if (!await queries.Visible().AnyAsync(a => a.Id == id, ctx.RequestAborted))
        {
            await ErrorCodes.WriteAsync(ctx, 404, "NOT_FOUND", "auction.not_found");
            return;
        }
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, life.ApplicationStopping);
        await sockets.RunAsync(id, socket, stop.Token);
    }
}

/// <summary>
/// Every few seconds, looks at the auctions somebody is listening to and announces changes: auction.started (upcoming → live),
/// auction.ended (with the winner), auction.extended (end date moved while running).
/// </summary>
public sealed class AuctionLifecycleWatcher(IServiceScopeFactory scopes, AuctionSockets sockets, TimeProvider clock, IConfiguration config,
    ILogger<AuctionLifecycleWatcher> log) : BackgroundService
{
    private sealed record Seen(AuctionPhase Phase, DateTime? EndsAtUtc);

    private readonly Dictionary<int, Seen> _seen = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Realtime:PollSeconds", 3), 1, 60)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Auction lifecycle check failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var watched = sockets.WatchedAuctions;
        foreach (var gone in _seen.Keys.Except(watched).ToList()) _seen.Remove(gone);
        if (watched.Count == 0) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Auctions.AsNoTracking().Where(a => watched.Contains(a.Id))
            .Select(a => new
            {
                a.Id, a.StartsAtUtc, a.EndsAtUtc, a.CurrentPrice,
                Winner = db.Bids.Where(b => b.AuctionId == a.Id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => (int?)b.UserId).FirstOrDefault(),
            }).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var a in rows)
        {
            var phase = AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, now);
            var current = new Seen(phase, a.EndsAtUtc);
            if (_seen.TryGetValue(a.Id, out var before))
            {
                if (before.Phase == AuctionPhase.Upcoming && phase != AuctionPhase.Upcoming)
                    await sockets.BroadcastAsync(a.Id, "auction.started", new { });
                if (phase != AuctionPhase.Ended && a.EndsAtUtc != before.EndsAtUtc && a.EndsAtUtc is not null)
                    await sockets.BroadcastAsync(a.Id, "auction.extended", new { endDate = a.EndsAtUtc });
                if (before.Phase != AuctionPhase.Ended && phase == AuctionPhase.Ended)
                    await sockets.BroadcastAsync(a.Id, "auction.ended", new { winnerUserId = a.Winner, finalPrice = a.CurrentPrice });
            }
            _seen[a.Id] = current;
        }
    }
}

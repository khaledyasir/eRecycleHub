using System.Collections.Concurrent;
using ERecycleHub.Application;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Api;

/// <summary>
/// Live auction room. Clients call <c>Watch(auctionId)</c> and receive <c>bid.placed</c> (see PublicEndpoints) for that auction.
/// Anonymous on purpose: the payload is the same public price/count the details page already shows. Bids still go through
/// POST /api/auctions/{id}/bids; the hub is receive-only. After a reconnect, clients refetch the auction details.
/// Only publicly visible auctions can be watched (like the mobile socket), and one connection watches at most <see cref="MaxPerConnection"/>.
/// </summary>
public sealed class AuctionHub(AuctionQueries queries) : Hub
{
    public const int MaxPerConnection = 20;

    public static string Room(int auctionId) => $"auction-{auctionId}";

    // ponytail: in-memory, like AuctionSockets (single API instance); scale-out needs a backplane and a shared registry.
    // Keys are bounded by the number of visible auctions (Watch checks it), so empty rooms are left in place.
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> Watchers = new();

    /// <summary>Auctions with at least one hub listener: AuctionLifecycleWatcher sends them auction.started / auction.extended / auction.ended.</summary>
    public static IReadOnlyList<int> WatchedAuctions => Watchers.Where(r => !r.Value.IsEmpty).Select(r => r.Key).ToList();

    /// <summary>The auctions this connection watches, so a disconnect only cleans up its own entries.</summary>
    private HashSet<int> Mine => (HashSet<int>)(Context.Items.TryGetValue(nameof(Mine), out var v) ? v! : Context.Items[nameof(Mine)] = new HashSet<int>());

    public async Task Watch(int auctionId)
    {
        var mine = Mine;
        lock (mine)
        {
            if (!mine.Contains(auctionId) && mine.Count >= MaxPerConnection) throw new HubException("watch.too_many");
        }
        if (!await queries.Visible().AnyAsync(a => a.Id == auctionId, Context.ConnectionAborted)) throw new HubException("auction.not_found");
        lock (mine) mine.Add(auctionId);
        Watchers.GetOrAdd(auctionId, _ => new())[Context.ConnectionId] = 0;
        await Groups.AddToGroupAsync(Context.ConnectionId, Room(auctionId));
    }

    public Task Unwatch(int auctionId)
    {
        var mine = Mine;
        lock (mine) mine.Remove(auctionId);
        Forget(auctionId, Context.ConnectionId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(auctionId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var mine = Mine;
        int[] ids;
        lock (mine) ids = [.. mine];
        foreach (var auctionId in ids) Forget(auctionId, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static void Forget(int auctionId, string connectionId)
    {
        if (Watchers.TryGetValue(auctionId, out var room)) room.TryRemove(connectionId, out _);
    }
}

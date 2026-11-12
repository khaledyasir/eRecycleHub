using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ERecycleHub.Api;

/// <summary>
/// Live auction room. Clients call <c>Watch(auctionId)</c> and receive <c>bid.placed</c> (see PublicEndpoints) for that auction.
/// Anonymous on purpose: the payload is the same public price/count the details page already shows. Bids still go through
/// POST /api/auctions/{id}/bids; the hub is receive-only. After a reconnect, clients refetch the auction details.
/// </summary>
public sealed class AuctionHub : Hub
{
    public static string Room(int auctionId) => $"auction-{auctionId}";

    // ponytail: in-memory, like AuctionSockets (single API instance); scale-out needs a backplane and a shared registry.
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> Watchers = new();

    /// <summary>Auctions with at least one hub listener: AuctionLifecycleWatcher sends them auction.started / auction.extended / auction.ended.</summary>
    public static IReadOnlyList<int> WatchedAuctions => Watchers.Where(r => !r.Value.IsEmpty).Select(r => r.Key).ToList();

    public Task Watch(int auctionId)
    {
        Watchers.GetOrAdd(auctionId, _ => new())[Context.ConnectionId] = 0;
        return Groups.AddToGroupAsync(Context.ConnectionId, Room(auctionId));
    }

    public Task Unwatch(int auctionId)
    {
        Forget(auctionId, Context.ConnectionId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(auctionId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var auctionId in Watchers.Keys) Forget(auctionId, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static void Forget(int auctionId, string connectionId)
    {
        if (Watchers.TryGetValue(auctionId, out var room)) room.TryRemove(connectionId, out _);
    }
}

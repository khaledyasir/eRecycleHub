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

    public Task Watch(int auctionId) => Groups.AddToGroupAsync(Context.ConnectionId, Room(auctionId));
    public Task Unwatch(int auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(auctionId));
}

using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace ERecycleHub.Api.Mobile;

public sealed record PlaceBidRequest(decimal Amount);

/// <summary>§3.2 public content, §3.3 home and categories, §3.4 auctions and bidding, §3.5 favourites.</summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this RouteGroupBuilder v1)
    {
        // ---- public ----
        v1.MapGet("/countries", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.CountriesAsync(ct)))
            .AllowAnonymous().WithTags("Content").Produces<Envelope<List<MobileCountryDto>>>();
        v1.MapGet("/contents", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.ContentsAsync(ct)))
            .AllowAnonymous().WithTags("Content").Produces<Envelope<List<ContentDto>>>()
            .WithDescription("Fixed ids: 1 About, 2 Terms, 3 Privacy, 4 Payment. A page with no content yet is left out.");

        // ---- signed in ----
        v1.MapGet("/contact-us", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.ContactUsAsync(ct)))
            .WithTags("Content").Produces<Envelope<ContactUsDto>>();
        v1.MapGet("/faqs", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.FaqsAsync(ct)))
            .WithTags("Content").Produces<Envelope<List<FaqItemDto>>>();
        v1.MapGet("/home/slides", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.SlidesAsync(ct)))
            .WithTags("Home").Produces<Envelope<List<SlideDto>>>();
        v1.MapGet("/categories", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.CategoriesAsync(ct)))
            .WithTags("Home").Produces<Envelope<List<CategoryDto>>>();

        var a = v1.MapGroup("/auctions").WithTags("Auctions");
        a.MapGet("", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.ItemsAsync(ct: ct)))
            .Produces<Envelope<List<AuctionItemDto>>>().WithSummary("Every visible auction that has not ended (no paging; the app filters and sorts). Ended auctions are only returned in /profile/auctions (won) and favourites, and by id");
        // Registered as a literal segment; {id:int} below can never match "statuses".
        a.MapGet("/statuses", async (MobileCatalog c, CancellationToken ct) => Reply.Data(await c.StatusesAsync(ct)))
            .Produces<Envelope<List<AuctionStatusDto>>>().WithSummary("Auctions grouped by status, in tab order: All, Active, Upcoming, Expired. Ended auctions are not returned, so Expired is always empty");
        a.MapGet("/{id:int}", async (int id, HttpContext ctx, MobileCatalog c, CancellationToken ct) =>
                Reply.Data(await c.DetailsAsync(id, ctx.UserId(), ct) ?? throw AppException.NotFound("auction")))
            .Produces<Envelope<AuctionDetailsDto>>()
            .WithDescription("isLeading: you hold the highest bid. heldByViewer: what your leading bid holds (0 if none). debitsWallet: wallet bidding is on. wallet: your available balance (when wallet bidding is on). isWinner: the auction ended, was not voided, and you hold the winning bid. saleStatus (ended auctions with bids, else null): Paid | PaymentFailed | Pending | Voided | NotCharged; only the holder of the winning bid sees PaymentFailed, Voided or NotCharged, everyone else sees Paid or Pending.");
        a.MapPost("/{id:int}/bids", PlaceBidAsync).RequireRateLimiting(RateLimits.Bids).Produces<Envelope<AuctionDetailsDto>>()
            .WithSummary("Place a bid; returns the full auction details")
            .WithDescription("When the server runs with Bidding__DebitWallet=true a bid HOLDS its amount: the wallet balance and ledger are not touched, the previous leader's hold is released, and the money is charged only if you win when the auction ends. " +
                "The response also carries availableAfter (your available balance right after this bid) and releasedFromPrevious (the hold released from the previous leader, 0 if none), plus isLeading, heldByViewer and debitsWallet. "+
                "The wallet field of the returned auction is then your available balance (balance minus pending withdrawals minus the money held by your leading bids). You may lead several auctions while the sum of your holds fits it. " +
                "Errors: 409 AUCTION_CLOSED (auction ended, inactive or deactivated; = AUCTION_ENDED), 409 AUCTION_NOT_STARTED, 409 OUTBID (someone bid first: reload), 422 BID_TOO_LOW, " +
                "422 INSUFFICIENT_WALLET (= INSUFFICIENT_BALANCE: your available balance cannot cover the hold; the message states required and available; also returned when it is below the auction's insurance), " +
                "403 BID_NOT_ALLOWED (own auction, administrator or blocked account). Listeners of /api/ws/auctions/{id} get bid.placed with the same bid id.");

        var f = v1.MapGroup("/favorites").WithTags("Favorites");
        f.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.FavoritesAsync(ctx.UserId(), ct)))
            .Produces<Envelope<List<AuctionItemDto>>>();
        f.MapPost("/{auctionId:int}", async (int auctionId, HttpContext ctx, AccountService acc, CancellationToken ct) =>
        {
            await acc.SetFavoriteAsync(ctx.UserId(), auctionId, true, ct);
            return Reply.Done();
        }).Produces<Envelope<object>>().WithDescription("Idempotent. 404 when the auction does not exist.");
        f.MapDelete("/{auctionId:int}", async (int auctionId, HttpContext ctx, AccountService acc, CancellationToken ct) =>
        {
            await acc.SetFavoriteAsync(ctx.UserId(), auctionId, false, ct);
            return Reply.Done();
        }).Produces<Envelope<object>>().WithDescription("Idempotent.");
    }

    private static async Task<IResult> PlaceBidAsync(int id, PlaceBidRequest req, HttpContext ctx, BiddingService bidding, MobileCatalog catalog,
        AuctionSockets sockets, IHubContext<AuctionHub> hub, TimeProvider clock, AppDbContext db, CancellationToken ct)
    {
        var userId = ctx.UserId();
        BidPlaced placed;
        try
        {
            placed = await bidding.PlaceBidAsync(userId, id, req.Amount, ct);
        }
        catch (AppException e) when (e.Code == BidErrors.BelowMinimum)
        {
            // The app only sends amounts ≥ the price it shows plus the increment. If the price has since reached the amount, someone bid first.
            var current = await db.Auctions.Where(x => x.Id == id).Select(x => x.CurrentPrice).FirstOrDefaultAsync(ct);
            throw req.Amount <= current ? new AppException("bid.outbid", 409) : e;
        }
        catch (AppException e) when (e.Code == BidErrors.NotFound)
        {
            throw AppException.NotFound("auction");
        }

        // The bid is committed; a failed push must not turn it into an error for the bidder (listeners reload on reconnect).
        var evt = new BidEventDto(placed.CurrentPrice, placed.BidCount, new LatestBidDto(placed.BidId, placed.UserId, placed.CurrentPrice, placed.CreatedAtUtc));
        try { await sockets.BroadcastAsync(id, "bid.placed", evt); } catch (Exception) { }
        try { await hub.Clients.Group(AuctionHub.Room(id)).SendAsync("bid.placed", new { placed.AuctionId, placed.CurrentPrice, placed.MinimumNextBid, BidsCount = placed.BidCount, placed.BidId, ServerTime = clock.GetUtcNow().UtcDateTime }, CancellationToken.None); } catch (Exception) { }

        var details = await catalog.DetailsAsync(id, userId, ct) ?? throw AppException.NotFound("auction");
        return Reply.Data(details with { AvailableAfter = placed.AvailableAfter, ReleasedFromPrevious = placed.ReleasedFromPrevious });
    }
}

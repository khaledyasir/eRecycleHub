using ERecycleHub.Application;

namespace ERecycleHub.Api.Endpoints;

public sealed record ActiveRequest(bool Active);

/// <summary>Auction management shared by the seller portal and the admin dashboard (the service scopes by caller).</summary>
public static class AuctionManagementEndpoints
{
    public static void MapManagedAuctions(this RouteGroupBuilder g)
    {
        g.MapGet("/auctions", (HttpContext ctx, AuctionManagement m, CancellationToken ct, string? view, string? status, string? q, int? publisherId,
                DateTime? from, DateTime? to, int page = 1, int pageSize = 20) =>
            m.ListAsync(ctx.Actor(), new ManagedAuctionFilter(view ?? "active", status, q, publisherId, from.AsUtc(), to.AsUtc(), page, pageSize), ct));
        g.MapGet("/auctions/{id:int}", (int id, HttpContext ctx, AuctionManagement m, CancellationToken ct) => m.GetAsync(ctx.Actor(), id, ct));
        g.MapPost("/auctions", async (HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<AuctionInput>(ct);
            var id = await m.CreateAsync(ctx.Actor(), Utc(data), Uploads(files), ct);
            if (files.Uploads("images") is { Count: > 0 } images) await m.AddImagesAsync(ctx.Actor(), id, images, ct);
            return Results.Created($"/api/auctions/{id}", new IdResponse(id));
        }).Accepts<AuctionInput>("multipart/form-data").Produces<IdResponse>(201);
        g.MapPut("/auctions/{id:int}", async (int id, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<AuctionInput>(ct);
            await m.UpdateAsync(ctx.Actor(), id, Utc(data), Uploads(files), ct);
            return Results.NoContent();
        }).Accepts<AuctionInput>("multipart/form-data");
        g.MapPost("/auctions/{id:int}/active", async (int id, ActiveRequest req, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            await m.SetActiveAsync(ctx.Actor(), id, req.Active, ct);
            return Results.NoContent();
        });
        g.MapDelete("/auctions/{id:int}", async (int id, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            await m.DeleteAsync(ctx.Actor(), id, ct);
            return Results.NoContent();
        });
        g.MapPost("/auctions/{id:int}/images", async (int id, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            if (!ctx.Request.HasFormContentType) throw new AppException("request.multipart_required", 415);
            var form = await ctx.Request.ReadFormAsync(ct);
            return await m.AddImagesAsync(ctx.Actor(), id, form.Files.Uploads("images"), ct);
        }).Accepts<IFormFileCollection>("multipart/form-data").Produces<List<ImageDto>>();
        g.MapDelete("/auctions/{id:int}/images/{imageId:int}", async (int id, int imageId, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
        {
            await m.DeleteImageAsync(ctx.Actor(), id, imageId, ct);
            return Results.NoContent();
        });
        g.MapGet("/auctions/{id:int}/bids", (int id, int? page, HttpContext ctx, AuctionManagement m, CancellationToken ct) =>
            m.BidsAsync(ctx.Actor(), id, new PageQuery(page ?? 1, 50), ct));
    }

    private static AuctionInput Utc(AuctionInput i) => i with
    {
        StartsAtUtc = ((DateTime?)i.StartsAtUtc).AsUtc()!.Value,
        EndsAtUtc = ((DateTime?)i.EndsAtUtc).AsUtc()!.Value,
    };

    private static AuctionUploads Uploads(IFormFileCollection f) =>
        new(f.Upload("mainImage"), f.Upload("terms"), f.Upload("paymentConditions"), f.Upload("contract"));
}

public static class SellerEndpoints
{
    public static void MapSellerEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/seller").WithTags("Seller").RequireAuthorization(Policies.Publisher);
        g.MapManagedAuctions();
        g.MapGet("/dashboard", (HttpContext ctx, SellerService s, CancellationToken ct) => s.DashboardAsync(ctx.Actor(), ct));
        g.MapGet("/reviews", (int? page, HttpContext ctx, SellerService s, CancellationToken ct) => s.ReviewsAsync(ctx.Actor(), new PageQuery(page ?? 1, 20), ct));
        g.MapDelete("/reviews/{id:int}", async (int id, HttpContext ctx, SellerService s, CancellationToken ct) => { await s.DeleteReviewAsync(ctx.Actor(), id, ct); return Results.NoContent(); });
        g.MapGet("/bidders", (string? q, int? page, HttpContext ctx, SellerService s, CancellationToken ct) => s.BiddersAsync(ctx.Actor(), q, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/bidders/{userId:int}", (int userId, HttpContext ctx, SellerService s, CancellationToken ct) => s.BidderAsync(ctx.Actor(), userId, ct));
        g.MapGet("/profile", (HttpContext ctx, SellerService s, CancellationToken ct) => s.ProfileAsync(ctx.Actor(), ct));
        g.MapPut("/profile", async (HttpContext ctx, SellerService s, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<StoreProfileInput>(ct);
            return await s.UpdateProfileAsync(ctx.Actor(), data, new StoreProfileFiles(files.Upload("logo"), files.Upload("background"),
                files.Upload("crDocument"), files.Upload("vatCertificate"), files.Upload("idDocument")), ct);
        }).Accepts<StoreProfileInput>("multipart/form-data").Produces<StoreProfileDto>();
    }
}

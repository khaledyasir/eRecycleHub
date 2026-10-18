using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ERecycleHub.Api.Endpoints;

public sealed record PlaceBidRequest(decimal Amount);
public sealed record IdResponse(int Id);

public static class PublicEndpoints
{
    private static int? Country(HttpRequest req, int? explicitId) => explicitId is > 0 ? explicitId : explicitId == 0 ? null : req.CountryCookie();

    public static void MapPublicEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("").WithTags("Public").AllowAnonymous();

        g.MapGet("/home", (int? countryId, HttpContext ctx, PublicContent content, CancellationToken ct) =>
            content.HomeAsync(Country(ctx.Request, countryId), ctx.ActorOrNull()?.UserId, ct));
        g.MapGet("/countries", (PublicContent content, CancellationToken ct) => content.CountriesAsync(ct));
        g.MapGet("/categories", (int? countryId, HttpContext ctx, PublicContent content, CancellationToken ct) => content.CategoriesAsync(Country(ctx.Request, countryId), ct));

        g.MapGet("/auctions", (HttpContext ctx, AuctionQueries queries, CancellationToken ct, string? status, string? q, [FromQuery(Name = "category")] int[]? category,
            decimal? minPrice, decimal? maxPrice, string? period, string? sort, int? countryId, int? storeId, int page = 1, int pageSize = 12) =>
            queries.SearchAsync(new AuctionFilter(status, q, category, minPrice, maxPrice, period, sort, Country(ctx.Request, countryId), storeId, page, pageSize),
                ctx.ActorOrNull()?.UserId, ct));
        g.MapGet("/auctions/facets", (int? countryId, int? storeId, HttpContext ctx, AuctionQueries q, CancellationToken ct) =>
            q.FacetsAsync(Country(ctx.Request, countryId), storeId, ct));
        g.MapGet("/auctions/calendar", (DateTime from, DateTime to, int? countryId, HttpContext ctx, AuctionQueries q, CancellationToken ct) =>
            q.CalendarAsync(((DateTime?)from).AsUtc()!.Value, ((DateTime?)to).AsUtc()!.Value, Country(ctx.Request, countryId), ct));
        g.MapGet("/auctions/{id:int}", async (int id, HttpContext ctx, AuctionQueries q, CancellationToken ct) =>
            await q.DetailsAsync(id, ctx.ActorOrNull(), ct) is { } d ? Results.Ok(d) : Results.Problem(statusCode: 404, title: "auction.not_found", extensions: new Dictionary<string, object?> { ["code"] = "auction.not_found" }))
            .Produces<AuctionDetails>();
        g.MapGet("/auctions/{id:int}/bids", (int id, int? page, HttpContext ctx, AuctionQueries q, CancellationToken ct) =>
            q.PublicBidsAsync(id, ctx.ActorOrNull()?.UserId, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/auctions/{id:int}/reviews", (int id, int? page, AuctionQueries q, CancellationToken ct) => q.AuctionReviewsAsync(id, page ?? 1, ct));

        // --- member actions on an auction ---
        var m = api.MapGroup("/auctions/{id:int}").WithTags("Public").RequireAuthorization(Policies.Member);
        m.MapPost("/bids", (int id, PlaceBidRequest req, HttpContext ctx, BiddingService bidding, CancellationToken ct) =>
            bidding.PlaceBidAsync(ctx.Actor().UserId, id, req.Amount, ct)).RequireRateLimiting(RateLimits.Bids);
        m.MapPut("/favorite", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.SetFavoriteAsync(ctx.Actor().UserId, id, true, ct); return Results.NoContent(); });
        m.MapDelete("/favorite", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.SetFavoriteAsync(ctx.Actor().UserId, id, false, ct); return Results.NoContent(); });
        m.MapPut("/watch", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.SetWatchAsync(ctx.Actor().UserId, id, true, ct); return Results.NoContent(); });
        m.MapDelete("/watch", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.SetWatchAsync(ctx.Actor().UserId, id, false, ct); return Results.NoContent(); });
        m.MapPost("/reviews", async (int id, ReviewInput req, HttpContext ctx, AccountService a, CancellationToken ct) =>
            new IdResponse(await a.AddReviewAsync(ctx.Actor().UserId, id, req, ct))).RequireRateLimiting(RateLimits.Forms);

        g.MapGet("/stores", (string? q, int? countryId, int? page, HttpContext ctx, PublicContent c, CancellationToken ct) =>
            c.StoresAsync(Country(ctx.Request, countryId), q, new PageQuery(page ?? 1, 12), ct));
        g.MapGet("/stores/{id:int}", async (int id, PublicContent c, CancellationToken ct) =>
            await c.StoreAsync(id, ct) is { } s ? Results.Ok(s) : Results.Problem(statusCode: 404, title: "store.not_found", extensions: new Dictionary<string, object?> { ["code"] = "store.not_found" }))
            .Produces<StoreDetails>();
        g.MapGet("/clients", (int? page, PublicContent c, CancellationToken ct) => c.ClientsAsync(new PageQuery(page ?? 1, 24), ct));

        g.MapGet("/content/about", (PublicContent c, CancellationToken ct) => c.AboutAsync(ct));
        g.MapGet("/content/benefits", (PublicContent c, CancellationToken ct) => c.BenefitsAsync(ct));
        g.MapGet("/content/how-to", (PublicContent c, CancellationToken ct) => c.HowToAsync(ct));
        g.MapGet("/content/faq", (PublicContent c, CancellationToken ct) => c.FaqAsync(ct));
        g.MapGet("/content/contact-info", async (int? countryId, HttpContext ctx, PublicContent c, CancellationToken ct) =>
            await c.ContactInfoAsync(Country(ctx.Request, countryId), ct) is { } info ? Results.Ok(info) : Results.NoContent()).Produces<ContactInfoDto>();
        g.MapGet("/content/settings/{key}", async (string key, PublicContent c, CancellationToken ct) =>
            await c.SettingAsync(key, ct) is { } s ? Results.Ok(s) : Results.Problem(statusCode: 404, title: "setting.not_found", extensions: new Dictionary<string, object?> { ["code"] = "setting.not_found" }))
            .Produces<SettingDto>();
        g.MapGet("/i18n/{locale}", (string locale, PublicContent c, CancellationToken ct) => c.TranslationOverridesAsync(locale, ct));
        g.MapGet("/sitemap", async (PublicContent c, CancellationToken ct) => { var (a, s) = await c.SitemapAsync(ct); return new { auctions = a, stores = s }; });

        g.MapPost("/contact", async (ContactInput req, PublicContent c, CancellationToken ct) => { await c.SubmitContactAsync(req, ct); return Results.NoContent(); })
            .RequireRateLimiting(RateLimits.Forms);

        g.MapGet("/files/{id:guid}", ServeFileAsync);
    }

    /// <summary>
    /// Serves uploaded bytes. Private files (KYC documents, payment proofs) only to their owner and admins; anything the caller may not
    /// read is a 404 so ids cannot be probed. Never rendered as HTML: nosniff, sandboxed CSP, attachment for office formats.
    /// </summary>
    private static async Task<IResult> ServeFileAsync(Guid id, HttpContext ctx, FileService files, CancellationToken ct)
    {
        var found = await files.OpenAsync(id, ctx.ActorOrNull(), ct);
        if (found is null) return Results.NotFound();
        var (f, path) = found.Value;
        var h = ctx.Response.Headers;
        var isPdf = f.ContentType == "application/pdf";
        var isImage = f.ContentType.StartsWith("image/", StringComparison.Ordinal);
        h.ContentSecurityPolicy = isPdf ? "default-src 'none'; frame-ancestors 'self'" : "default-src 'none'; img-src 'self'; frame-ancestors 'self'; sandbox";
        h.XFrameOptions = "SAMEORIGIN";
        h.CacheControl = f.IsPublic ? "public, max-age=31536000, immutable" : "private, no-store";
        var inline = isPdf || isImage;
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(f.OriginalName);
        h.ContentDisposition = disposition.ToString();
        return Results.File(path, f.ContentType, enableRangeProcessing: true);
    }
}

using ERecycleHub.Application;

namespace ERecycleHub.Api.Endpoints;

public sealed record WalletSummary(decimal Balance, int PendingRequests, decimal PendingAmount, decimal MaxTopUp);
public sealed record TopUpRequest(decimal Amount, string? IdempotencyKey);
public sealed record CountResponse(int Count);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/account").WithTags("Account").RequireAuthorization(Policies.Member);

        g.MapGet("/dashboard", (HttpContext ctx, AccountService a, CancellationToken ct) => a.DashboardAsync(ctx.Actor().UserId, ct));
        g.MapGet("/bids", (int? page, HttpContext ctx, AccountService a, CancellationToken ct) => a.BidsAsync(ctx.Actor().UserId, new PageQuery(page ?? 1, 10), ct));
        g.MapGet("/auctions", (string? tab, int? page, HttpContext ctx, AccountService a, CancellationToken ct) =>
            a.MyAuctionsAsync(ctx.Actor().UserId, tab == "won" ? "won" : "leading", new PageQuery(page ?? 1, 12), ct));
        g.MapGet("/favorites", (int? page, HttpContext ctx, AccountService a, CancellationToken ct) => a.FavoritesAsync(ctx.Actor().UserId, new PageQuery(page ?? 1, 12), ct));
        g.MapGet("/watching", (int? page, HttpContext ctx, AccountService a, CancellationToken ct) => a.WatchingAsync(ctx.Actor().UserId, new PageQuery(page ?? 1, 12), ct));

        g.MapGet("/profile", (HttpContext ctx, AccountService a, CancellationToken ct) => a.ProfileAsync(ctx.Actor().UserId, ct));
        g.MapPut("/profile", async (HttpContext ctx, AccountService a, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<ProfileInput>(ct);
            return await a.UpdateProfileAsync(ctx.Actor().UserId, data,
                new ProfileFiles(files.Upload("avatar"), files.Upload("crDocument"), files.Upload("vatCertificate"), files.Upload("idDocument")), ct);
        }).Accepts<ProfileInput>("multipart/form-data").Produces<ProfileDto>();

        g.MapGet("/wallet", async (HttpContext ctx, AccountService a, WalletService w, CancellationToken ct) =>
        {
            var id = ctx.Actor().UserId;
            var pending = await w.ListAsync(id, ERecycleHub.Domain.WalletTransactionStatus.Pending, null, null, null, new PageQuery(1, 100), ct);
            return new WalletSummary((await a.DashboardAsync(id, ct)).WalletBalance, pending.Total, pending.Items.Sum(t => t.Amount), WalletService.MaxTopUp);
        });
        g.MapGet("/wallet/transactions", (int? page, HttpContext ctx, WalletService w, CancellationToken ct) =>
            w.ListAsync(ctx.Actor().UserId, null, null, null, null, new PageQuery(page ?? 1, 10), ct));
        g.MapGet("/wallet/ledger", (int? page, HttpContext ctx, WalletService w, CancellationToken ct) => w.LedgerAsync(ctx.Actor().UserId, new PageQuery(page ?? 1, 10), ct));
        g.MapPost("/wallet/top-ups", async (HttpContext ctx, WalletService w, FileService files, ERecycleHub.Infrastructure.AppDbContext db, CancellationToken ct) =>
        {
            var userId = ctx.Actor().UserId;
            var (data, form) = await ctx.Request.ReadMultipartAsync<TopUpRequest>(ct);
            Guid? proof = null;
            if (form.Upload("proof") is { } upload)
            {
                proof = (await files.SaveAsync(upload, ERecycleHub.Domain.FileCategory.TransactionProof, userId, ct)).Id;
                await db.SaveChangesAsync(ct);
            }
            return await w.RequestTopUpAsync(userId, data.Amount, proof, data.IdempotencyKey, ct);
        }).RequireRateLimiting(RateLimits.Forms).Accepts<TopUpRequest>("multipart/form-data").Produces<WalletTransactionDto>();

        g.MapGet("/notifications", (bool? unread, int? page, HttpContext ctx, AccountService a, CancellationToken ct) =>
            a.NotificationsAsync(ctx.Actor().UserId, unread == true, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/notifications/unread-count", async (HttpContext ctx, AccountService a, CancellationToken ct) => new CountResponse(await a.UnreadCountAsync(ctx.Actor().UserId, ct)));
        g.MapPost("/notifications/{id:int}/read", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.MarkReadAsync(ctx.Actor().UserId, id, ct); return Results.NoContent(); });
        g.MapPost("/notifications/read-all", async (HttpContext ctx, AccountService a, CancellationToken ct) => { await a.MarkReadAsync(ctx.Actor().UserId, null, ct); return Results.NoContent(); });
        g.MapDelete("/notifications/{id:int}", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.DeleteNotificationAsync(ctx.Actor().UserId, id, ct); return Results.NoContent(); });
    }
}

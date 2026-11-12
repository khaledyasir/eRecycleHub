using ERecycleHub.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ERecycleHub.Api.Endpoints;

/// <summary>The web wallet. <c>Available</c> = balance − pending withdrawals − held: what can be bid or withdrawn (the mobile <c>/wallet/balance</c> figures).
/// <c>PendingRequests</c>/<c>PendingAmount</c> are top-ups waiting for approval. <c>DebitsWallet</c> says whether bids hold money (server switch).</summary>
public sealed record WalletSummary(decimal Balance, int PendingRequests, decimal PendingAmount, decimal MaxTopUp,
    decimal Held, decimal PendingWithdrawals, decimal Available, bool DebitsWallet);
public sealed record WithdrawalRequest(decimal Amount, string? IdempotencyKey);
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
        // The app's DELETE /api/v1/profile for the website: same rules (409 account.delete_blocked while money is unsettled), then the cookie session ends.
        g.MapDelete("/profile", async (HttpContext ctx, ERecycleHub.Application.Mobile.MobileAccount m, CancellationToken ct) =>
        {
            var actor = ctx.Actor();
            if (actor.Role == ERecycleHub.Domain.UserRole.Admin) throw AppException.Forbidden();
            await m.DeleteAccountAsync(actor.UserId, ct);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new MeResponse(null, Security.IssueCsrfToken(ctx, new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())));
        }).RequireRateLimiting(RateLimits.Forms);

        // ---- become a seller: customers apply with their documents; an admin approves (see /admin/applications) ----
        g.MapGet("/seller-application", (HttpContext ctx, AccountService a, CancellationToken ct) => a.SellerStatusAsync(ctx.Actor(), ct));
        g.MapPost("/seller-application", async (HttpContext ctx, AccountService a, CancellationToken ct) =>
        {
            var (data, files) = await ctx.Request.ReadMultipartAsync<SellerApplicationInput>(ct);
            var id = await a.SubmitSellerApplicationAsync(ctx.Actor(), data,
                new SellerApplicationFiles(files.Upload("crDocument"), files.Upload("vatCertificate"), files.Upload("idDocument")), ct);
            return Results.Created($"/api/account/seller-application", new IdResponse(id));
        }).RequireRateLimiting(RateLimits.Forms).Accepts<SellerApplicationInput>("multipart/form-data").Produces<IdResponse>(201);

        g.MapGet("/wallet", async (HttpContext ctx, WalletService w, IBiddingSettings bidding, CancellationToken ct) =>
        {
            var id = ctx.Actor().UserId;
            var b = await w.BalanceAsync(id, ct);
            var pending = await w.ListAsync(id, ERecycleHub.Domain.WalletTransactionStatus.Pending, null, null, null, new PageQuery(1, 100), ct);
            var topUps = pending.Items.Where(t => t.Type == ERecycleHub.Domain.WalletTransactionType.Credit).ToList();
            return new WalletSummary(b.Balance, topUps.Count, topUps.Sum(t => t.Amount), WalletService.MaxTopUp, b.Held, b.PendingWithdrawals, b.Available, bidding.DebitWallet);
        });
        // Same rules as the app's POST /api/v1/wallet/withdraw: counts against the available balance until an administrator pays it out.
        g.MapPost("/wallet/withdrawals", (WithdrawalRequest req, HttpContext ctx, WalletService w, CancellationToken ct) =>
            w.RequestWithdrawalAsync(ctx.Actor().UserId, req.Amount, req.IdempotencyKey, ct)).RequireRateLimiting(RateLimits.Forms).Produces<WalletTransactionDto>();
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
        }).RequireRateLimiting(RateLimits.Money).Accepts<TopUpRequest>("multipart/form-data").Produces<WalletTransactionDto>();

        g.MapGet("/notifications", (bool? unread, int? page, HttpContext ctx, AccountService a, CancellationToken ct) =>
            a.NotificationsAsync(ctx.Actor().UserId, unread == true, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/notifications/unread-count", async (HttpContext ctx, AccountService a, CancellationToken ct) => new CountResponse(await a.UnreadCountAsync(ctx.Actor().UserId, ct)));
        g.MapPost("/notifications/{id:int}/read", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.MarkReadAsync(ctx.Actor().UserId, id, ct); return Results.NoContent(); });
        g.MapPost("/notifications/read-all", async (HttpContext ctx, AccountService a, CancellationToken ct) => { await a.MarkReadAsync(ctx.Actor().UserId, null, ct); return Results.NoContent(); });
        g.MapDelete("/notifications/{id:int}", async (int id, HttpContext ctx, AccountService a, CancellationToken ct) => { await a.DeleteNotificationAsync(ctx.Actor().UserId, id, ct); return Results.NoContent(); });
    }
}

using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;

namespace ERecycleHub.Api.Mobile;

public sealed record UpdateProfileRequest(string? FullName, string? Email, string? Phone);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record AmountRequest(decimal Amount);
public sealed record IdsRequest(int[]? Ids);
public sealed record FcmTokenRequest(string? Token, string? Platform = null, string? DeviceId = null);

/// <summary>§3.6 notifications and devices, §3.7 profile and my auctions, §3.8 wallet. All require a bearer token.</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this RouteGroupBuilder v1)
    {
        var p = v1.MapGroup("/profile").WithTags("Profile");
        p.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.ProfileAsync(ctx.UserId(), ct)))
            .Produces<Envelope<UserProfileDto>>();
        p.MapPut("", async (UpdateProfileRequest req, HttpContext ctx, MobileAccount m, CancellationToken ct) =>
            Reply.Data(await m.UpdateProfileAsync(ctx.UserId(), req.FullName, req.Email, req.Phone, ct)))
            .Produces<Envelope<UserProfileDto>>().WithDescription("Does not change the photo (POST /profile/image). Taken e-mail/phone: 422 VALIDATION_FAILED.");
        p.MapPost("/image", async (HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            if (!ctx.Request.HasFormContentType) throw new AppException("request.multipart_required", 415);
            var form = await ctx.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) throw MobileAccount.Invalid("file", "file.required");
            await using var stream = file.OpenReadStream();
            return Reply.Data(await m.SetImageAsync(ctx.UserId(), new UploadInput(stream, file.FileName, file.Length), ct));
        }).RequireRateLimiting(RateLimits.Forms).DisableAntiforgery().Accepts<IFormFile>("multipart/form-data").Produces<Envelope<ProfileImageDto>>()
            .WithDescription("multipart/form-data with the image in part `file` (JPEG, PNG, GIF or WebP; 10 MB at most). Saved on the account at once.");
        p.MapPost("/password", async (ChangePasswordRequest req, HttpContext ctx, AuthService auth, SessionService sessions, CancellationToken ct) =>
        {
            var userId = ctx.UserId();
            await auth.ChangePasswordAsync(userId, req.CurrentPassword ?? "", req.NewPassword ?? "", ct);
            await sessions.RevokeAllAsync(userId, keep: ctx.SessionId(), ct);
            return Reply.Done();
        }).RequireRateLimiting(RateLimits.Auth).Produces<Envelope<object>>()
            .WithDescription("Wrong current password: 422 INVALID_CREDENTIALS (not 401). This device stays signed in; other devices are signed out.");
        p.MapDelete("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            await m.DeleteAccountAsync(ctx.UserId(), ct);
            return Reply.Done();
        }).Produces<Envelope<object>>().WithDescription("Removes personal data, ends every session and push token. 409 ACCOUNT_DELETE_BLOCKED while the account has a wallet balance, a pending withdrawal or money held by a leading bid (the message and arguments say how much): withdraw or use the money first.");
        p.MapGet("/auctions", async (HttpContext ctx, MobileAccount m, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (pg, size) = MobilePaging.Of(page, pageSize);
                var r = await m.MyAuctionsAsync(ctx.UserId(), pg, size, ct);
                ctx.Response.Headers["X-Total-Count-Live"] = r.LiveTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ctx.Response.Headers["X-Total-Count-Won"] = r.WonTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ctx.TotalCount(r.LiveTotal + r.WonTotal);
                return Reply.Data(r.Data);
            }).Produces<Envelope<MyAuctionsDto>>().WithDescription("live: running auctions you bid on. won: ended auctions where your bid was highest. Optional page (from 1) and pageSize (default and max 100) apply to each list; totals are in X-Total-Count-Live and X-Total-Count-Won (X-Total-Count is their sum).");

        var w = v1.MapGroup("/wallet").WithTags("Wallet");
        w.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.WalletAsync(ctx.UserId(), ct)))
            .Produces<Envelope<WalletDto>>()
            .WithDescription("type: Deposit | Withdraw. status: Pending | Completed | Rejected | Reversed.");
        w.MapGet("/balance", async (HttpContext ctx, WalletService wallet, CancellationToken ct) => Reply.Data(await wallet.BalanceAsync(ctx.UserId(), ct)))
            .Produces<Envelope<WalletBalanceDto>>()
            .WithDescription("balance = money in the wallet. pendingWithdrawals = withdrawal requests awaiting approval. held = money reserved by your leading bids (only when wallet bidding is on). available = balance - pendingWithdrawals - held: what can be bid or withdrawn.");
        w.MapGet("/ledger", async (HttpContext ctx, WalletService wallet, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            var r = await wallet.LedgerAsync(ctx.UserId(), new PageQuery(page, pageSize), ct);
            return Reply.Data(new PagedResult<LedgerItemDto>(r.Items.Select(l => new LedgerItemDto(l.Id, l.Reason.ToString(), l.Amount, l.BalanceBefore,
                l.BalanceAfter, l.ReferenceType, l.ReferenceId, l.CreatedAtUtc)).ToList(), r.Page, r.PageSize, r.Total));
        }).Produces<Envelope<PagedResult<LedgerItemDto>>>()
            .WithDescription("Every balance change, newest first. amount is signed (negative = money out). balanceBefore is null on entries older than this field. pageSize max 100.");
        w.MapPost("/increase", async (AmountRequest req, HttpContext ctx, WalletService wallet, CancellationToken ct) =>
        {
            await wallet.RequestTopUpAsync(ctx.UserId(), req.Amount, null, ctx.Request.Headers["Idempotency-Key"].FirstOrDefault(), ct);
            return Reply.Done();
        }).RequireRateLimiting(RateLimits.Money).Produces<Envelope<object>>()
            .WithDescription("Creates a Pending deposit; an administrator credits the wallet after the bank transfer arrives and you get a push and an in-app notification (topup_approved / topup_rejected). At most 5 requests (deposits and withdrawals together) can be Pending at once: 409 WALLET_TOO_MANY_PENDING. Optional Idempotency-Key header (max 100 chars): a retry with the same key returns the same request instead of creating another; 409 wallet.idempotency_key_reused if the key belongs to a different kind of request.");
        w.MapPost("/withdraw", async (AmountRequest req, HttpContext ctx, WalletService wallet, CancellationToken ct) =>
        {
            await wallet.RequestWithdrawalAsync(ctx.UserId(), req.Amount, ctx.Request.Headers["Idempotency-Key"].FirstOrDefault(), ct);
            return Reply.Done();
        }).RequireRateLimiting(RateLimits.Money).Produces<Envelope<object>>()
            .WithDescription("Creates a Pending withdrawal; the balance is debited when an administrator approves it. 422 INSUFFICIENT_WALLET when the amount exceeds the available balance (balance - pending withdrawals - held). Approval later re-checks the same figure and notifies you (withdrawal_approved / withdrawal_rejected). Optional Idempotency-Key header: a retry with the same key and amount returns the same request; 409 wallet.idempotency_key_reused otherwise.");

        var n = v1.MapGroup("/notifications").WithTags("Notifications");
        n.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct, bool? unread = null, int? page = null, int? pageSize = null) =>
            {
                var (pg, size) = MobilePaging.Of(page, pageSize);
                var l = await m.NotificationsAsync(ctx.UserId(), unread == true, pg, size, ct);
                ctx.TotalCount(l.Total);
                return Reply.Data(l.Items);
            })
            .Produces<Envelope<List<AppNotificationDto>>>()
            .WithDescription("Newest first. Optional unread=true, page (from 1) and pageSize (default and max 100); the total is in the X-Total-Count header. type: auction | bid | wallet | system. kind is the exact event (the push `type` with the same name); auctionId says which auction to open. Each push carries notification_id = id of the row here, so a push and its row are one notification.");
        n.MapGet("/unread-count", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(new { count = await m.UnreadCountAsync(ctx.UserId(), ct) }))
            .Produces<Envelope<object>>();
        n.MapPost("/read-all", async (HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            await m.MarkAllReadAsync(ctx.UserId(), ct);
            return Reply.Done();
        }).Produces<Envelope<object>>();
        n.MapPost("/read", async (IdsRequest req, HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            await m.MarkReadAsync(ctx.UserId(), req.Ids, ct);
            return Reply.Done();
        }).Produces<Envelope<object>>();
        n.MapDelete("/{id:int}", async (int id, HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            await m.DeleteNotificationAsync(ctx.UserId(), id, ct);
            return Reply.Done();
        }).Produces<Envelope<object>>().WithDescription("Soft delete: POST /notifications/{id}/restore undoes it within 24 hours.");
        n.MapPost("/{id:int}/restore", async (int id, HttpContext ctx, MobileAccount m, CancellationToken ct) =>
        {
            await m.RestoreNotificationAsync(ctx.UserId(), id, ct);
            return Reply.Done();
        }).Produces<Envelope<object>>();

        v1.MapPost("/devices/fcm-token", async (FcmTokenRequest req, HttpContext ctx, SessionService sessions, CancellationToken ct) =>
        {
            await sessions.RegisterDeviceAsync(ctx.UserId(), ctx.SessionId(), req.Token, req.Platform, req.DeviceId, ctx.Request.Headers["lang"].ToString(), ct);
            return Reply.Done();
        }).WithTags("Notifications").Produces<Envelope<object>>().WithDescription("Upsert; re-sending the same token is fine. platform: android | ios (optional). deviceId (optional, max 100): a new token with the same deviceId replaces that device's old token. The backend subscribes the token to the topics of the auctions you take part in; the app does not. Logout forgets the token.");
    }
}

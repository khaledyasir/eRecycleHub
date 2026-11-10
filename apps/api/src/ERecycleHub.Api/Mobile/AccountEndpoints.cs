using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;

namespace ERecycleHub.Api.Mobile;

public sealed record UpdateProfileRequest(string? FullName, string? Email, string? Phone);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record AmountRequest(decimal Amount);
public sealed record IdsRequest(int[]? Ids);
public sealed record FcmTokenRequest(string? Token);

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
        }).Produces<Envelope<object>>().WithDescription("Removes personal data, ends every session and push token.");
        p.MapGet("/auctions", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.MyAuctionsAsync(ctx.UserId(), ct)))
            .Produces<Envelope<MyAuctionsDto>>().WithDescription("live: running auctions you bid on. won: ended auctions where your bid was highest.");

        var w = v1.MapGroup("/wallet").WithTags("Wallet");
        w.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.WalletAsync(ctx.UserId(), ct)))
            .Produces<Envelope<WalletDto>>()
            .WithDescription("type: Deposit | Withdraw. status: Pending | Completed | Rejected | Reversed.");
        w.MapPost("/increase", async (AmountRequest req, HttpContext ctx, WalletService wallet, CancellationToken ct) =>
        {
            await wallet.RequestTopUpAsync(ctx.UserId(), req.Amount, null, null, ct);
            return Reply.Done();
        }).RequireRateLimiting(RateLimits.Forms).Produces<Envelope<object>>()
            .WithDescription("Creates a Pending deposit; an administrator credits the wallet after the bank transfer arrives.");
        w.MapPost("/withdraw", async (AmountRequest req, HttpContext ctx, WalletService wallet, CancellationToken ct) =>
        {
            await wallet.RequestWithdrawalAsync(ctx.UserId(), req.Amount, ct);
            return Reply.Done();
        }).RequireRateLimiting(RateLimits.Forms).Produces<Envelope<object>>()
            .WithDescription("Creates a Pending withdrawal; the balance is debited when an administrator approves it. 422 INSUFFICIENT_WALLET when the amount exceeds the balance minus pending withdrawals.");

        var n = v1.MapGroup("/notifications").WithTags("Notifications");
        n.MapGet("", async (HttpContext ctx, MobileAccount m, CancellationToken ct) => Reply.Data(await m.NotificationsAsync(ctx.UserId(), ct)))
            .Produces<Envelope<List<AppNotificationDto>>>().WithDescription("Newest first, at most 500. type: auction | bid | wallet | system.");
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
            await sessions.RegisterDeviceAsync(ctx.UserId(), ctx.SessionId(), req.Token, ct);
            return Reply.Done();
        }).WithTags("Notifications").Produces<Envelope<object>>().WithDescription("Upsert; re-sending the same token is fine. Logout forgets it.");
    }
}

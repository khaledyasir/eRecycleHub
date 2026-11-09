using System.Text.Json;
using ERecycleHub.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Api.Mobile;

/// <summary>Every mobile response: <c>{ success, message, data }</c> (docs/API_REQUIREMENTS.md §3.0).</summary>
public sealed record Envelope<T>(bool Success, string? Message, T? Data);

/// <summary>Every failure: <c>{ success: false, message, errorCode, errors }</c>. The app shows a text chosen by <c>errorCode</c>, never <c>message</c>.</summary>
public sealed record ErrorEnvelope(bool Success, string? Message, string ErrorCode, IDictionary<string, string[]>? Errors);

public static class Reply
{
    public static IResult Data<T>(T data) => TypedResults.Ok(new Envelope<T>(true, null, data));
    public static IResult Done() => TypedResults.Ok(new Envelope<object>(true, null, null));
}

/// <summary>Maps service error codes (AppException) to the app's errorCode values and HTTP statuses (§4.2).</summary>
public static class ErrorCodes
{
    public sealed record Mapped(int Status, string ErrorCode, IDictionary<string, string[]>? Errors = null);

    private static Mapped Field(string field, string message) => new(422, "VALIDATION_FAILED", new Dictionary<string, string[]> { [field] = [message] });

    public static Mapped Map(string code, int status, object? args) => code switch
    {
        "validation.failed" => new(422, "VALIDATION_FAILED", args as IDictionary<string, string[]>),
        "auth.invalid_credentials" => new(401, "INVALID_CREDENTIALS"),
        // A wrong current password must not be 401: on a protected path that signs the user out.
        "auth.current_password_wrong" => new(422, "INVALID_CREDENTIALS", new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is wrong."] }),
        "auth.locked" or "otp.too_many" or "rate.limited" => new(429, "RATE_LIMITED"),
        "auth.required" => new(401, "UNAUTHORIZED"),
        "auth.forbidden" => new(403, "FORBIDDEN"),
        "auth.password_policy" or "auth.password_spaces" => Field("password", "Use at least 8 characters, without spaces."),
        "auth.email_taken" => Field("email", "The email has already been taken."),
        "auth.reset_token_invalid" => new(422, "INVALID_RESET_TOKEN"),
        "phone.invalid" => Field("phone", "Enter a valid Jordanian mobile number."),
        "country.invalid" => Field("countryId", "Unknown country."),
        "otp.invalid" => new(422, "INVALID_OTP"),
        "otp.not_verified" => new(422, "OTP_NOT_VERIFIED"),
        "otp.unavailable" => new(503, "OTP_UNAVAILABLE"),
        "bid.ended" or "bid.inactive" => new(409, "AUCTION_CLOSED"),
        "bid.not_started" => new(409, "AUCTION_NOT_STARTED"),
        "bid.below_minimum" => new(422, "BID_TOO_LOW"),
        "bid.outbid" => new(409, "OUTBID"),
        "bid.insufficient_insurance" or "wallet.insufficient" => new(422, "INSUFFICIENT_WALLET"),
        "bid.own_auction" or "bid.admin_cannot_bid" or "bid.account_blocked" => new(403, "BID_NOT_ALLOWED"),
        "bid.invalid_amount" or "wallet.invalid_amount" => Field("amount", "Enter a positive amount with at most two decimals."),
        "device.token_invalid" => Field("token", "A push token is required."),
        "request.too_large" => new(413, "PAYLOAD_TOO_LARGE"),
        "server.error" => new(500, "SERVER_ERROR"),
        _ when code.StartsWith("request.", StringComparison.Ordinal) => new(422, "VALIDATION_FAILED"),
        _ when code.StartsWith("file.", StringComparison.Ordinal) => Field("file", code switch
        {
            "file.too_large" => "The file is too large (10 MB at most).",
            "file.type_not_allowed" => "Upload a JPEG, PNG, GIF or WebP image.",
            _ => "The file is not a valid image.",
        }),
        _ when code.EndsWith(".not_found", StringComparison.Ordinal) => new(404, "NOT_FOUND"),
        _ => new(status, code.ToUpperInvariant().Replace('.', '_')),
    };

    public static string ForStatus(int status) => status switch
    {
        401 => "UNAUTHORIZED",
        403 => "FORBIDDEN",
        404 => "NOT_FOUND",
        405 => "METHOD_NOT_ALLOWED",
        413 => "PAYLOAD_TOO_LARGE",
        415 or 400 or 422 => "VALIDATION_FAILED",
        429 => "RATE_LIMITED",
        >= 500 => "SERVER_ERROR",
        _ => "HTTP_" + status,
    };

    /// <summary>Seconds the client should wait, for 429s that carry it in their arguments.</summary>
    public static int? RetryAfter(object? args) => args switch
    {
        null => null,
        _ when args.GetType().GetProperty("retryAfterSeconds")?.GetValue(args) is int s => s,
        _ when args.GetType().GetProperty("minutes")?.GetValue(args) is int m => m * 60,
        _ => null,
    };

    public static async Task WriteAsync(HttpContext ctx, int status, string errorCode, string? message, IDictionary<string, string[]>? errors = null)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = status;
        var options = ctx.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        await ctx.Response.WriteAsJsonAsync(new ErrorEnvelope(false, message, errorCode, errors), options, ctx.RequestAborted);
    }
}

/// <summary>Turns every exception into the error envelope. Business errors keep their code; anything unexpected is logged and hidden.</summary>
public sealed class EnvelopeExceptionHandler(ILogger<EnvelopeExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        // The client went away: nobody to answer. The database driver reports this as a SqlException, so test the request token itself.
        if (ctx.RequestAborted.IsCancellationRequested) return true;
        (string code, int status, object? args) = ex switch
        {
            AppException app => (app.Code, app.Status, app.Args),
            BadHttpRequestException bad => (bad.StatusCode == 413 ? "request.too_large" : "request.invalid", bad.StatusCode, null),
            JsonException => ("request.invalid_json", 400, null),
            _ => ("server.error", 500, null),
        };
        if (status == 500) log.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        var mapped = ErrorCodes.Map(code, status, args);
        if (mapped.Status == 429 && ErrorCodes.RetryAfter(args) is int seconds) ctx.Response.Headers.RetryAfter = seconds.ToString();
        await ErrorCodes.WriteAsync(ctx, mapped.Status, mapped.ErrorCode, code, mapped.Errors);
        return true;
    }
}

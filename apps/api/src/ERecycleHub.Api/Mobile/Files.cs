using ERecycleHub.Application;
using ERecycleHub.Application.Mobile;
using Microsoft.Net.Http.Headers;

namespace ERecycleHub.Api.Mobile;

/// <summary>
/// Absolute file URLs: <c>{App:ApiPublicUrl}/api/v1/files/{id}</c>, or the current request's scheme and host when that setting is empty
/// (forwarded headers are honoured).
/// </summary>
public sealed class FileUrls(IHttpContextAccessor http, IConfiguration config) : IFileUrls
{
    private string? _base;

    private string Base => _base ??= (config["App:ApiPublicUrl"] is { Length: > 0 } configured
        ? configured
        : http.HttpContext is { } ctx ? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}" : "").TrimEnd('/');

    public string? Of(Guid? fileId) => fileId is null ? null : $"{Base}/api/v1/files/{fileId.Value}";
}

public static class FileEndpoints
{
    public static void MapFileEndpoints(this RouteGroupBuilder v1) =>
        v1.MapGet("/files/{id:guid}", ServeAsync).AllowAnonymous().WithTags("Files")
            .WithSummary("Uploaded file bytes (images, auction documents)")
            .WithDescription("Public files need no token (the app's image loader and WebView do not send one). Private files are served to their owner only; anything else is 404.");

    private static async Task<IResult> ServeAsync(Guid id, HttpContext ctx, FileService files, CancellationToken ct)
    {
        var found = await files.OpenAsync(id, ctx.ActorOrNull(), ct);
        if (found is null) throw AppException.NotFound("file");
        var (f, path) = found.Value;
        var h = ctx.Response.Headers;
        var isPdf = f.ContentType == "application/pdf";
        var isImage = f.ContentType.StartsWith("image/", StringComparison.Ordinal);
        // Never rendered as an active document: nosniff (global), sandboxed CSP, attachment for office formats.
        h.ContentSecurityPolicy = isPdf ? "default-src 'none'" : "default-src 'none'; img-src 'self'; sandbox";
        h.CacheControl = f.IsPublic ? "public, max-age=31536000, immutable" : "private, no-store";
        var disposition = new ContentDispositionHeaderValue(isPdf || isImage ? "inline" : "attachment");
        disposition.SetHttpFileName(f.OriginalName);
        h.ContentDisposition = disposition.ToString();
        return Results.File(path, f.ContentType, enableRangeProcessing: true);
    }
}

using System.Security.Claims;
using System.Text.Json;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Api;

public static class Claims
{
    public const string Stamp = "erh:stamp";
    public const string PublisherId = "erh:pid";

    public static ClaimsPrincipal Principal(SessionUser u, string scheme)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Email),
            new(ClaimTypes.Role, u.Role.ToString()),
            new(Stamp, u.SecurityStamp),
        ];
        if (u.PublisherId is int pid) claims.Add(new(PublisherId, pid.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }
}

public static class HttpExtensions
{
    /// <summary>The signed-in caller, or null for anonymous requests.</summary>
    public static Actor? ActorOrNull(this HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated != true || !int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        if (!Enum.TryParse<UserRole>(u.FindFirstValue(ClaimTypes.Role), out var role)) return null;
        int? pid = int.TryParse(u.FindFirstValue(Claims.PublisherId), out var p) ? p : null;
        return new Actor(id, role, pid, ctx.Connection.RemoteIpAddress?.ToString());
    }

    /// <summary>The signed-in caller. Endpoints using this are behind an authorisation policy, so null here is a bug, not a user error.</summary>
    public static Actor Actor(this HttpContext ctx) => ctx.ActorOrNull() ?? throw new AppException("auth.required", 401);

    public static bool IsArabic(this HttpRequest req) => req.Headers["X-Locale"].ToString() == "ar";

    public static int? CountryCookie(this HttpRequest req) =>
        int.TryParse(req.Cookies["erh_country"], out var id) && id > 0 ? id : null;

    public static UploadInput? Upload(this IFormFileCollection files, string name)
    {
        var f = files.GetFile(name);
        return f is null || f.Length == 0 ? null : new UploadInput(f.OpenReadStream(), f.FileName, f.Length);
    }

    public static List<UploadInput> Uploads(this IFormFileCollection files, string name) =>
        files.GetFiles(name).Where(f => f.Length > 0).Select(f => new UploadInput(f.OpenReadStream(), f.FileName, f.Length)).ToList();

    /// <summary>Multipart convention: structured fields as JSON in the "data" part, files as named parts.</summary>
    public static async Task<(T data, IFormFileCollection files)> ReadMultipartAsync<T>(this HttpRequest req, CancellationToken ct)
    {
        if (!req.HasFormContentType) throw new AppException("request.multipart_required", 415);
        var form = await req.ReadFormAsync(ct);
        var json = form["data"].ToString();
        if (string.IsNullOrWhiteSpace(json)) throw new AppException("request.data_required");
        var options = req.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        try
        {
            var data = JsonSerializer.Deserialize<T>(json, options) ?? throw new AppException("request.data_required");
            return (data, form.Files);
        }
        catch (JsonException)
        {
            throw new AppException("request.invalid_json");
        }
    }

    public static DateTime? AsUtc(this DateTime? value) => value is null ? null : value.Value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };
}

/// <summary>Maps business errors to RFC 7807 problem details with a stable <c>code</c>; hides everything else.</summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        int status;
        string code;
        object? args = null;
        switch (ex)
        {
            // The browser went away (navigation, closed tab): there is nobody to answer. The database driver reports this as a
            // SqlException rather than an OperationCanceledException, so test the request token itself.
            case not null when ctx.RequestAborted.IsCancellationRequested:
                return true;
            case AppException app:
                (status, code, args) = (app.Status, app.Code, app.Args);
                break;
            case BadHttpRequestException bad:
                (status, code) = (bad.StatusCode, bad.StatusCode == 413 ? "request.too_large" : "request.invalid");
                break;
            case OperationCanceledException when ctx.RequestAborted.IsCancellationRequested:
                return true;
            default:
                log.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                (status, code) = (500, "server.error");
                break;
        }
        ctx.Response.StatusCode = status;
        var details = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = code };
        details.Extensions["code"] = code;
        if (args is not null) details.Extensions["args"] = args;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = details });
    }
}

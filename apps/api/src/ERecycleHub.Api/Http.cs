using System.Security.Claims;
using ERecycleHub.Api.Mobile;
using ERecycleHub.Application;
using ERecycleHub.Domain;

namespace ERecycleHub.Api;

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

    /// <summary>The signed-in caller. Endpoints using this require authentication, so null here is a bug, not a user error.</summary>
    public static Actor Actor(this HttpContext ctx) => ctx.ActorOrNull() ?? throw new AppException("auth.required", 401);

    public static int UserId(this HttpContext ctx) => ctx.Actor().UserId;

    /// <summary>The session behind the bearer token of this request.</summary>
    public static Guid SessionId(this HttpContext ctx) =>
        Guid.TryParse(ctx.User.FindFirstValue(Claims.SessionId), out var id) ? id : throw new AppException("auth.required", 401);

    /// <summary>The app sends <c>lang: en|ar</c> on every request (a custom header, not Accept-Language).</summary>
    public static bool IsArabic(this HttpRequest req) => string.Equals(req.Headers["lang"].ToString(), "ar", StringComparison.OrdinalIgnoreCase);
}

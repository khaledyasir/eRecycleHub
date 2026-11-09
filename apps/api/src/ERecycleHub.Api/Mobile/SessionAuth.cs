using System.Security.Claims;
using System.Text.Encodings.Web;
using ERecycleHub.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Api.Mobile;

/// <summary>
/// <c>Authorization: Bearer &lt;token&gt;</c> → the session row. Checked against the database on every request, so logout, password
/// changes, deactivation and account deletion take effect immediately. Challenges and denials answer with the error envelope.
/// </summary>
public sealed class SessionAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var sessions = Context.RequestServices.GetRequiredService<SessionService>();
        var session = await sessions.ValidateAsync(header[7..].Trim(), Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("Invalid or expired token.");
        return AuthenticateResult.Success(new AuthenticationTicket(Claims.Principal(session.User, session.SessionId, SchemeName), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ErrorCodes.WriteAsync(Context, 401, "UNAUTHORIZED", "auth.required");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ErrorCodes.WriteAsync(Context, 403, "FORBIDDEN", "auth.forbidden");
}

public static class Claims
{
    public const string SessionId = "erh:sid";
    public const string PublisherId = "erh:pid";

    public static ClaimsPrincipal Principal(SessionUser u, Guid sessionId, string scheme)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Email),
            new(ClaimTypes.Role, u.Role.ToString()),
            new(SessionId, sessionId.ToString()),
        ];
        if (u.PublisherId is int pid) claims.Add(new(PublisherId, pid.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }
}

using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

/// <summary>
/// Reads the Firebase service-account key from configuration only: the <c>Firebase__Credentials</c> environment variable (or secret) holding the
/// JSON, or the same JSON base64-encoded (easier for some secret stores). There is deliberately no file path option in this app and the
/// value is never logged.
/// </summary>
public static class FirebaseCredentials
{
    /// <summary>The credential JSON, decoded from base64 if needed; null when nothing usable is configured.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.StartsWith('{')) return v;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(v)).Trim();
            return decoded.StartsWith('{') ? decoded : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public sealed class FirebasePushGateway : IPushGateway
{
    private readonly FirebaseMessaging _messaging;

    private FirebasePushGateway(FirebaseMessaging messaging) => _messaging = messaging;

    /// <summary>Creates the gateway from <c>Firebase:Credentials</c>; null (and a log line that never contains the key) when absent or invalid.</summary>
    public static FirebasePushGateway? TryCreate(IConfiguration config, ILogger log)
    {
        var json = FirebaseCredentials.Normalize(config["Firebase:Credentials"]);
        if (json is null) return null;
        try
        {
            var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential() });
            return new FirebasePushGateway(FirebaseMessaging.GetMessaging(app));
        }
        catch (Exception ex)
        {
            // The exception text could quote the key material: log only its type.
            log.LogError("Firebase credentials could not be loaded ({ErrorType}); push is disabled.", ex.GetType().Name);
            return null;
        }
    }

    public async Task<TopicResult> SubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default) =>
        Map(tokens, await _messaging.SubscribeToTopicAsync(tokens, topic));

    public async Task<TopicResult> UnsubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default) =>
        Map(tokens, await _messaging.UnsubscribeFromTopicAsync(tokens, topic));

    public async Task<TopicResult> SendAsync(IReadOnlyList<PushTarget> targets, CancellationToken ct = default)
    {
        var dead = new List<string>();
        foreach (var batch in targets.Chunk(PushBatching.MaxTokensPerCall / 2))   // SendEach accepts 500 messages
        {
            var response = await _messaging.SendEachAsync(batch.Select(t => ToMessage(t.Message, t.Token, null)), ct);
            for (var i = 0; i < response.Responses.Count; i++)
                if (!response.Responses[i].IsSuccess && response.Responses[i].Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                    dead.Add(batch[i].Token);
        }
        return dead.Count == 0 ? TopicResult.None : new TopicResult(dead);
    }

    public Task SendToTopicAsync(string topic, PushMessage message, CancellationToken ct = default) =>
        _messaging.SendAsync(ToMessage(message, null, topic), ct);

    // Message.Token is marked obsolete in favour of Fid (an installation id); the app registers FCM registration tokens, which is what Token carries.
#pragma warning disable CS0618
    private static Message ToMessage(PushMessage m, string? token, string? topic) => new()
    {
        Token = token,
        Topic = topic,
        Notification = new Notification { Title = m.Title, Body = m.Body },
        Data = m.Data,
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            TimeToLive = TimeSpan.FromSeconds(m.TtlSeconds),
            Notification = new AndroidNotification { Tag = m.CollapseKey },
        },
        Apns = new ApnsConfig
        {
            Headers = m.CollapseKey is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["apns-collapse-id"] = m.CollapseKey },
            Aps = new Aps { Sound = "default" },
        },
    };
#pragma warning restore CS0618

    /// <summary>The IID service answers per token; these reasons mean the token itself is dead, anything else is transient.</summary>
    public static bool IsDeadTokenReason(string? reason) =>
        reason is not null && (reason.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("invalid-argument", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("not-registered", StringComparison.OrdinalIgnoreCase));

    private static TopicResult Map(IReadOnlyList<string> tokens, TopicManagementResponse response) =>
        response.FailureCount == 0
            ? TopicResult.None
            : new TopicResult(response.Errors.Where(e => IsDeadTokenReason(e.Reason) && e.Index >= 0 && e.Index < tokens.Count).Select(e => tokens[e.Index]).ToList());
}

public static class PushRegistration
{
    /// <summary>Registers Firebase when credentials are configured, otherwise the no-op gateway (with a warning outside Development).</summary>
    public static IServiceCollection AddPush(this IServiceCollection services, IConfiguration config, bool warnWhenMissing) =>
        services.AddSingleton<IPushGateway>(sp =>
        {
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Push");
            if (FirebasePushGateway.TryCreate(config, log) is { } firebase) return firebase;
            if (warnWhenMissing) log.LogWarning("Firebase:Credentials is not set (or invalid): push subscriptions and notifications are disabled.");
            return new NoopPushGateway(sp.GetRequiredService<ILogger<NoopPushGateway>>());
        });
}

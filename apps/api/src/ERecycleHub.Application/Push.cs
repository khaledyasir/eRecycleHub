using System.Globalization;
using ERecycleHub.Application.Mobile;
using ERecycleHub.Domain;
using Microsoft.Extensions.Logging;

namespace ERecycleHub.Application;

/// <summary>Tokens a push service reported as dead (unregistered or malformed). Callers delete them.</summary>
public sealed record TopicResult(IReadOnlyList<string> InvalidTokens)
{
    public static readonly TopicResult None = new([]);
}

/// <summary>
/// The push service (Firebase Cloud Messaging). Implementations take at most <see cref="PushBatching.MaxTokensPerCall"/> tokens per call
/// and report dead tokens; they throw only for failures worth retrying or logging.
/// </summary>
public interface IPushGateway
{
    Task<TopicResult> SubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default);
    Task<TopicResult> UnsubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default);

    /// <summary>Sends one message per target (each device its own text). At most <see cref="PushBatching.MaxTokensPerCall"/> per call. Reports dead tokens.</summary>
    Task<TopicResult> SendAsync(IReadOnlyList<PushTarget> targets, CancellationToken ct = default);

    /// <summary>Sends one message to everyone subscribed to the topic.</summary>
    Task SendToTopicAsync(string topic, PushMessage message, CancellationToken ct = default);
}

/// <summary>
/// What a device receives: a visible notification (<see cref="Title"/>, <see cref="Body"/>) plus a data map of strings the app uses to route the tap.
/// <see cref="CollapseKey"/> makes a newer push of the same kind replace the older one on the device.
/// </summary>
public sealed record PushMessage(string Type, int? AuctionId, string Title, string Body, IReadOnlyDictionary<string, string> Data, string? CollapseKey, int TtlSeconds);

public sealed record PushTarget(string Token, PushMessage Message);

public static class PushBatching
{
    /// <summary>FCM topic management accepts at most 1000 registration tokens per request.</summary>
    public const int MaxTokensPerCall = 1000;

    public static IEnumerable<string[]> Split(IEnumerable<string> tokens) => tokens.Distinct().Chunk(MaxTokensPerCall);
}

/// <summary>Used when no Firebase credentials are configured (development, tests): nothing is sent and every token counts as valid.</summary>
public sealed class NoopPushGateway(ILogger<NoopPushGateway> log) : IPushGateway
{
    public Task<TopicResult> SubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default)
    {
        log.LogDebug("Push disabled: would subscribe {Count} token(s) to {Topic}", tokens.Count, topic);
        return Task.FromResult(TopicResult.None);
    }

    public Task<TopicResult> UnsubscribeAsync(IReadOnlyList<string> tokens, string topic, CancellationToken ct = default)
    {
        log.LogDebug("Push disabled: would unsubscribe {Count} token(s) from {Topic}", tokens.Count, topic);
        return Task.FromResult(TopicResult.None);
    }

    public Task<TopicResult> SendAsync(IReadOnlyList<PushTarget> targets, CancellationToken ct = default)
    {
        log.LogDebug("Push disabled: would send {Count} message(s)", targets.Count);
        return Task.FromResult(TopicResult.None);
    }

    public Task SendToTopicAsync(string topic, PushMessage message, CancellationToken ct = default)
    {
        log.LogDebug("Push disabled: would send {Type} to {Topic}", message.Type, topic);
        return Task.CompletedTask;
    }
}

/// <summary>Builds the push for a notification: the same data the in-app row has, so a tap can open the right screen. All data values are strings.</summary>
public static class PushPayloads
{
    /// <summary>The push <c>type</c>, finer than the in-app list's chip (auction | bid | wallet | system).</summary>
    public static string TypeOf(string kind) => kind switch
    {
        NotificationKinds.NewBid => "new_bid",
        NotificationKinds.Outbid => "outbid",
        NotificationKinds.Joined => "joined",
        NotificationKinds.AuctionWon => "auction_won",
        NotificationKinds.Ended => "auction_ended",
        NotificationKinds.PaymentFailed => "payment_failed",
        NotificationKinds.TopUpApproved => "topup_approved",
        NotificationKinds.TopUpRejected => "topup_rejected",
        NotificationKinds.WithdrawalApproved => "withdrawal_approved",
        NotificationKinds.WithdrawalRejected => "withdrawal_rejected",
        NotificationKinds.CreditReversed => "credit_reversed",
        _ => kind.Replace('.', '_'),
    };

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <param name="language">"en" or anything else for Arabic (the default).</param>
    /// <param name="charged">What was charged to the wallet (only for a won auction).</param>
    public static PushMessage Build(string kind, int auctionId, int? notificationId, string? language, string? auctionEn, string? auctionAr, decimal? amount, decimal? charged, DateTime eventAtUtc)
    {
        var t = NotificationTexts.For(kind, auctionEn, auctionAr, amount, null, null);
        var en = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var type = TypeOf(kind);
        var data = new Dictionary<string, string>
        {
            ["type"] = type,
            ["auction_id"] = auctionId.ToString(CultureInfo.InvariantCulture),
            ["title"] = en ? t.TitleEn : t.TitleAr,
            ["body"] = en ? t.BodyEn : t.BodyAr,
            ["title_en"] = t.TitleEn, ["title_ar"] = t.TitleAr, ["body_en"] = t.BodyEn, ["body_ar"] = t.BodyAr,
            ["event_at"] = eventAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        if (notificationId is int nid) data["notification_id"] = nid.ToString(CultureInfo.InvariantCulture);
        if (amount is decimal a) data["amount"] = Money(a);
        if (charged is decimal c) data["charged"] = Money(c);
        return new PushMessage(type, auctionId, data["title"], data["body"], data, CollapseKey(type, auctionId), Ttl(type));
    }

    /// <summary>
    /// A wallet decision (top-up or withdrawal approved/rejected, credit reversed). Same shape as the auction pushes, but there is no auction, so
    /// <c>auction_id</c> is left out and <c>transaction_id</c> says which wallet request it is.
    /// </summary>
    public static PushMessage BuildWallet(string kind, int transactionId, int? notificationId, string? language, decimal? amount, DateTime eventAtUtc)
    {
        var t = NotificationTexts.For(kind, null, null, amount, null, null);
        var en = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var type = TypeOf(kind);
        var data = new Dictionary<string, string>
        {
            ["type"] = type,
            ["transaction_id"] = transactionId.ToString(CultureInfo.InvariantCulture),
            ["title"] = en ? t.TitleEn : t.TitleAr,
            ["body"] = en ? t.BodyEn : t.BodyAr,
            ["title_en"] = t.TitleEn, ["title_ar"] = t.TitleAr, ["body_en"] = t.BodyEn, ["body_ar"] = t.BodyAr,
            ["event_at"] = eventAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        if (notificationId is int nid) data["notification_id"] = nid.ToString(CultureInfo.InvariantCulture);
        if (amount is decimal a) data["amount"] = Money(a);
        return new PushMessage(type, null, data["title"], data["body"], data, $"wallet_{transactionId}", 86400);
    }

    /// <summary>The "auction ended" message for everyone on the auction's topic. One text for all devices, so it carries both languages.</summary>
    public static PushMessage BuildEndedForTopic(int auctionId, string? auctionEn, string? auctionAr, DateTime eventAtUtc)
    {
        var type = TypeOf(NotificationKinds.Ended);
        var title = "انتهى المزاد · Auction ended";
        var body = $"{auctionAr ?? auctionEn} · {auctionEn ?? auctionAr}";
        var data = new Dictionary<string, string>
        {
            ["type"] = type, ["auction_id"] = auctionId.ToString(CultureInfo.InvariantCulture), ["title"] = title, ["body"] = body,
            ["title_en"] = "Auction ended", ["title_ar"] = "انتهى المزاد", ["body_en"] = auctionEn ?? auctionAr ?? "", ["body_ar"] = auctionAr ?? auctionEn ?? "",
            ["event_at"] = eventAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        return new PushMessage(type, auctionId, title, body, data, CollapseKey(type, auctionId), Ttl(type));
    }

    /// <summary>A newer push of the same kind for the same auction replaces the older one on the device; a winner's push replaces the generic "ended" one.</summary>
    public static string CollapseKey(string type, int auctionId) => type switch
    {
        "auction_won" or "auction_ended" => $"auction_{auctionId}_end",
        _ => $"auction_{auctionId}_{type}",
    };

    private static int Ttl(string type) => type is "new_bid" or "outbid" ? 3600 : 86400;
}

using System.Globalization;
using ERecycleHub.Domain;

namespace ERecycleHub.Application;

/// <summary>
/// Pushes the notification an administrator's wallet decision wrote (top-up or withdrawal approved/rejected, credit reversed). The in-app row is written in
/// the decision's own transaction with <c>EventKey = wtx:{transactionId}:{approved|rejected|reversed}</c>, so a decision notifies once; this runs on the
/// background queue after the commit and uses the same pipeline as the auction notifications. Never moves money.
/// </summary>
public sealed class WalletNotifier(NotificationPusher pusher)
{
    public static string EventKey(int transactionId, WalletTransactionStatus outcome) =>
        $"wtx:{transactionId.ToString(CultureInfo.InvariantCulture)}:{outcome.ToString().ToLowerInvariant()}";

    public Task NotifyDecisionAsync(int transactionId, WalletTransactionStatus outcome, CancellationToken ct = default) =>
        pusher.PushPendingAsync([EventKey(transactionId, outcome)], null, null,
            (row, language, now) => PushPayloads.BuildWallet(row.Kind, transactionId, row.Id, language, row.Amount, now), ct);
}

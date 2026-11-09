using ERecycleHub.Domain;

namespace ERecycleHub.Application.Mobile;

/// <summary>Bilingual titles and bodies for stored notifications (rows keep a kind plus data, not text, except legacy rows).</summary>
public static class NotificationTexts
{
    public sealed record Text(string TitleEn, string TitleAr, string BodyEn, string BodyAr);

    /// <summary>The app's filter chips: auction | bid | wallet | system.</summary>
    public static string TypeOf(string kind) => kind switch
    {
        NotificationKinds.NewBid or NotificationKinds.Outbid or NotificationKinds.AuctionWon => "bid",
        _ when kind.StartsWith("auction.", StringComparison.Ordinal) => "auction",
        _ when kind.StartsWith("wallet.", StringComparison.Ordinal) => "wallet",
        _ => "system",
    };

    public static Text For(string kind, string? auctionEn, string? auctionAr, decimal? amount, string? title, string? body)
    {
        var aEn = auctionEn ?? "an auction";
        var aAr = auctionAr ?? auctionEn ?? "مزاد";
        var amt = amount is null ? "" : $"{amount:0.##} JOD";
        var amtAr = amount is null ? "" : $"{amount:0.##} دينار";
        return kind switch
        {
            NotificationKinds.NewBid => new("New bid", "مزايدة جديدة", $"A new bid of {amt} was placed on {aEn}.", $"تم تقديم مزايدة جديدة بقيمة {amtAr} على {aAr}."),
            NotificationKinds.Outbid => new("You were outbid", "تمت المزايدة عليك", $"Someone bid {amt} on {aEn}.", $"قام شخص بالمزايدة بمبلغ {amtAr} على {aAr}."),
            NotificationKinds.AuctionWon => new("You won the auction", "لقد فزت بالمزاد", $"Congratulations! You won {aEn} at {amt}.", $"تهانينا! لقد فزت بـ {aAr} بمبلغ {amtAr}."),
            NotificationKinds.TopUpApproved => new("Deposit approved", "تمت الموافقة على الإيداع", $"{amt} was added to your wallet.", $"تمت إضافة {amtAr} إلى محفظتك."),
            NotificationKinds.TopUpRejected => new("Deposit rejected", "تم رفض الإيداع", $"Your deposit request of {amt} was rejected.", $"تم رفض طلب الإيداع بقيمة {amtAr}."),
            NotificationKinds.WithdrawalApproved => new("Withdrawal sent", "تم تحويل السحب", $"Your withdrawal of {amt} was approved.", $"تمت الموافقة على سحب {amtAr}."),
            NotificationKinds.WithdrawalRejected => new("Withdrawal rejected", "تم رفض السحب", $"Your withdrawal request of {amt} was rejected.", $"تم رفض طلب سحب {amtAr}."),
            NotificationKinds.SellerApproved => new("Seller application approved", "تمت الموافقة على طلب البائع", "You can now publish auctions.", "يمكنك الآن نشر المزادات."),
            NotificationKinds.SellerRejected => new("Seller application", "طلب البائع", "Your seller application was not approved.", "لم تتم الموافقة على طلب البائع."),
            _ => new(title ?? "Notification", title ?? "إشعار", body ?? "", body ?? ""),
        };
    }
}

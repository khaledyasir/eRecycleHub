using System.Net;

namespace ERecycleHub.Application;

/// <summary>Plain, bilingual-aware transactional emails. All inserted values are HTML-encoded.</summary>
public static class EmailTemplates
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Layout(bool ar, string heading, string body) =>
        $"""
        <!doctype html><html lang="{(ar ? "ar" : "en")}" dir="{(ar ? "rtl" : "ltr")}"><body style="margin:0;background:#f4f6f5;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#1c2b26">
        <div style="max-width:560px;margin:24px auto;background:#fff;border-radius:12px;overflow:hidden;border:1px solid #e3e8e6">
        <div style="background:#0f5b46;color:#fff;padding:20px 24px;font-size:20px;font-weight:600">eRecycleHub</div>
        <div style="padding:24px"><h1 style="font-size:18px;margin:0 0 12px">{E(heading)}</h1>{body}</div>
        <div style="padding:16px 24px;color:#6b7a75;font-size:12px;border-top:1px solid #e3e8e6">{(ar ? "هذه رسالة آلية، يرجى عدم الرد عليها." : "This is an automated message. Please do not reply.")}</div>
        </div></body></html>
        """;

    public static (string subject, string html) PasswordReset(bool ar, string link) => ar
        ? ("إعادة تعيين كلمة المرور", Layout(true, "إعادة تعيين كلمة المرور",
            $"<p>استخدم الرابط التالي لاختيار كلمة مرور جديدة. ينتهي خلال ساعة ويعمل مرة واحدة فقط:</p><p><a href=\"{E(link)}\">{E(link)}</a></p><p>إذا لم تطلب ذلك فتجاهل هذه الرسالة.</p>"))
        : ("Reset your password", Layout(false, "Reset your password",
            $"<p>Use this link to choose a new password. It expires in one hour and works once:</p><p><a href=\"{E(link)}\">{E(link)}</a></p><p>If you did not ask for this, ignore this email.</p>"));

    public static (string subject, string html) ApplicationApproved(bool ar, string accountUrl) => ar
        ? ("تمت الموافقة على طلبك للنشر", Layout(true, "تمت الموافقة على طلبك", $"<p>يمكنك الآن إنشاء المزادات من حسابك.</p><p><a href=\"{E(accountUrl)}\">فتح بوابة البائع</a></p>"))
        : ("Your seller application was approved", Layout(false, "Your application was approved", $"<p>You can now create auctions from your account.</p><p><a href=\"{E(accountUrl)}\">Open the seller portal</a></p>"));

    public static (string subject, string html) ApplicationRejected(bool ar, string reason) => ar
        ? ("بخصوص طلبك للنشر", Layout(true, "بخصوص طلبك للنشر", $"<p>نشكرك على اهتمامك. لم تتم الموافقة على طلبك في الوقت الحالي.</p><p>السبب: {E(reason)}</p><p>يمكنك تصحيح البيانات وإعادة التقديم من حسابك.</p>"))
        : ("About your seller application", Layout(false, "About your seller application", $"<p>Thank you for your interest. Your application was not approved at this time.</p><p>Reason: {E(reason)}</p><p>You can correct your details and apply again from your account.</p>"));

    public static (string subject, string html) AuctionWon(bool ar, string userName, string title, string refNumber, decimal amount, DateTime? endedUtc, string url) => ar
        ? ($"تهانينا! لقد فزت بالمزاد: {title}", Layout(true, "تهانينا، لقد فزت بالمزاد",
            $"<p>عزيزي {E(userName)}،</p><p>يسعدنا إبلاغك بفوزك بالمزاد التالي:</p><ul><li>المزاد: {E(title)}</li><li>الرقم المرجعي: {E(refNumber)}</li><li>مبلغ الفوز: {amount:N2} دينار</li><li>تاريخ الانتهاء (UTC): {endedUtc:yyyy-MM-dd HH:mm}</li></ul><p><a href=\"{E(url)}\">عرض المزاد</a></p><p>يرجى التواصل معنا لاستكمال الإجراءات.</p>"))
        : ($"Congratulations! You won the auction: {title}", Layout(false, "Congratulations, you won the auction",
            $"<p>Dear {E(userName)},</p><p>We are pleased to inform you that you won the following auction:</p><ul><li>Auction: {E(title)}</li><li>Reference: {E(refNumber)}</li><li>Winning bid: JOD {amount:N2}</li><li>Ended (UTC): {endedUtc:yyyy-MM-dd HH:mm}</li></ul><p><a href=\"{E(url)}\">View the auction</a></p><p>Please contact us to complete the process.</p>"));
}

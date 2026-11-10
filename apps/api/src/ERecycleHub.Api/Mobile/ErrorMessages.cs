using System.Text.RegularExpressions;

namespace ERecycleHub.Api.Mobile;

/// <summary>
/// Human-readable error texts in English and Arabic (picked by the <c>lang</c> header) for the envelope's <c>message</c> and for each
/// entry of <c>errors</c>. Looked up by the internal code (e.g. <c>bid.below_minimum</c>), then by <c>errorCode</c>, then a generic text.
/// <c>{name}</c> placeholders are filled from the error's arguments.
/// </summary>
public static partial class ErrorMessages
{
    private static readonly Dictionary<string, (string En, string Ar)> Texts = new()
    {
        // ---- authentication & session ----
        ["auth.required"] = ("Your session has ended. Please sign in again.", "انتهت جلستك. يرجى تسجيل الدخول مرة أخرى."),
        ["auth.forbidden"] = ("You are not allowed to do this.", "غير مسموح لك بتنفيذ هذا الإجراء."),
        ["auth.invalid_credentials"] = ("Wrong email or password.", "البريد الإلكتروني أو كلمة المرور غير صحيحة."),
        ["auth.current_password_wrong"] = ("The current password is wrong.", "كلمة المرور الحالية غير صحيحة."),
        ["auth.locked"] = ("Too many failed attempts. Try again in {minutes} minutes.", "محاولات فاشلة كثيرة. حاول مرة أخرى بعد {minutes} دقيقة."),
        ["auth.password_policy"] = ("The password must have at least 8 characters.", "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل."),
        ["auth.password_spaces"] = ("The password must not contain spaces.", "يجب ألا تحتوي كلمة المرور على مسافات."),
        ["auth.email_taken"] = ("This email is already registered.", "هذا البريد الإلكتروني مسجل مسبقاً."),
        ["auth.reset_token_invalid"] = ("The reset link has expired or was already used. Please request a new code.", "انتهت صلاحية رمز إعادة التعيين أو تم استخدامه. يرجى طلب رمز جديد."),

        // ---- one-time codes ----
        ["otp.invalid"] = ("The verification code is wrong or has expired.", "رمز التحقق غير صحيح أو منتهي الصلاحية."),
        ["otp.not_verified"] = ("Please verify your phone number first.", "يرجى التحقق من رقم الهاتف أولاً."),
        ["otp.too_many"] = ("Too many codes were requested. Please try again later.", "تم طلب رموز كثيرة. يرجى المحاولة لاحقاً."),
        ["otp.unavailable"] = ("We could not send the code right now. Please try again later.", "تعذر إرسال الرمز حالياً. يرجى المحاولة لاحقاً."),

        // ---- field validation (keys used in "errors") ----
        ["validation.failed"] = ("Please check the entered data.", "يرجى التحقق من البيانات المدخلة."),
        ["name.required"] = ("Please enter your full name (200 characters at most).", "يرجى إدخال الاسم الكامل (200 حرف كحد أقصى)."),
        ["email.invalid"] = ("Please enter a valid email address.", "يرجى إدخال بريد إلكتروني صحيح."),
        ["email.taken"] = ("This email is already registered.", "هذا البريد الإلكتروني مسجل مسبقاً."),
        ["phone.invalid"] = ("Please enter a valid Jordanian mobile number.", "يرجى إدخال رقم هاتف أردني صحيح."),
        ["phone.taken"] = ("This phone number is already registered.", "رقم الهاتف هذا مسجل مسبقاً."),
        ["password.invalid"] = ("The password must have at least 8 characters, without spaces.", "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل وبدون مسافات."),
        ["country.invalid"] = ("Please choose a valid country.", "يرجى اختيار دولة صحيحة."),
        ["amount.invalid"] = ("Please enter a valid amount (positive, at most two decimals).", "يرجى إدخال مبلغ صحيح (موجب وبحد أقصى خانتين عشريتين)."),
        ["ids.too_many"] = ("Too many items at once (1000 at most).", "عدد العناصر كبير جداً (1000 كحد أقصى)."),
        ["token.required"] = ("The device token is missing.", "رمز الجهاز مفقود."),
        ["file.required"] = ("Please choose a photo.", "يرجى اختيار صورة."),
        ["file.too_large"] = ("The file is too large (10 MB at most).", "حجم الملف كبير جداً (10 ميغابايت كحد أقصى)."),
        ["file.type_not_allowed"] = ("Please upload a JPEG, PNG, GIF or WebP image.", "يرجى رفع صورة بصيغة JPEG أو PNG أو GIF أو WebP."),
        ["file.empty"] = ("The file is empty.", "الملف فارغ."),
        ["file.content_mismatch"] = ("The file is not a valid image.", "الملف ليس صورة صالحة."),
        ["device.token_invalid"] = ("The device token is missing.", "رمز الجهاز مفقود."),
        ["request.invalid"] = ("The request is not valid.", "الطلب غير صالح."),
        ["request.invalid_json"] = ("The request is not valid.", "الطلب غير صالح."),
        ["request.multipart_required"] = ("Please send the photo as a file upload.", "يرجى إرسال الصورة كملف مرفق."),
        ["request.websocket_required"] = ("This address only accepts live (WebSocket) connections.", "هذا العنوان يقبل اتصالات البث المباشر فقط."),
        ["request.too_large"] = ("The upload is too large.", "حجم الملف المرفوع كبير جداً."),

        // ---- auctions & bidding ----
        ["bid.ended"] = ("This auction has ended.", "انتهى هذا المزاد."),
        ["bid.inactive"] = ("This auction is closed.", "هذا المزاد مغلق."),
        ["bid.not_started"] = ("This auction has not started yet.", "لم يبدأ هذا المزاد بعد."),
        ["bid.below_minimum"] = ("Your bid must be at least {minimum}.", "يجب أن تكون مزايدتك {minimum} على الأقل."),
        ["bid.outbid"] = ("Someone placed a higher bid. The price has been updated.", "قام شخص آخر بمزايدة أعلى. تم تحديث السعر."),
        ["bid.insufficient_insurance"] = ("Your wallet balance must be at least {insurance} to bid on this auction.", "يجب أن يكون رصيد محفظتك {insurance} على الأقل للمزايدة على هذا المزاد."),
        ["bid.own_auction"] = ("You cannot bid on your own auction.", "لا يمكنك المزايدة على مزادك الخاص."),
        ["bid.admin_cannot_bid"] = ("Administrator accounts cannot bid.", "لا يمكن لحسابات المسؤولين المزايدة."),
        ["bid.account_blocked"] = ("Your account cannot bid at the moment.", "لا يمكن لحسابك المزايدة حالياً."),
        ["bid.invalid_amount"] = ("Please enter a valid bid amount.", "يرجى إدخال مبلغ مزايدة صحيح."),

        // ---- wallet ----
        ["wallet.invalid_amount"] = ("Please enter a valid amount (up to {max}).", "يرجى إدخال مبلغ صحيح (حتى {max})."),
        ["bid.insufficient_balance"] = ("This bid needs {required} but only {available} of your wallet is available (money held by your other leading bids and pending withdrawals is not available).", "تحتاج هذه المزايدة إلى {required} لكن المتاح من محفظتك {available} فقط (المبالغ المحجوزة لمزايداتك الأخرى وطلبات السحب المعلّقة غير متاحة)."),
        ["wallet.insufficient"] = ("Your wallet balance is not enough.", "رصيد محفظتك غير كافٍ."),
        ["wallet.idempotency_key_reused"] = ("This request key was already used for a different request.", "تم استخدام مفتاح الطلب هذا لطلب مختلف."),
        ["wallet.self_approval_not_allowed"] = ("You cannot approve your own request; another administrator must.", "لا يمكنك الموافقة على طلبك الخاص؛ يجب أن يوافق مسؤول آخر."),
        ["wallet.self_credit_not_allowed"] = ("You cannot credit your own wallet; another administrator must.", "لا يمكنك إضافة رصيد إلى محفظتك؛ يجب أن يقوم بذلك مسؤول آخر."),
        ["wallet.owner_inactive"] = ("This account is inactive or deleted, so the request cannot be approved or reversed.", "هذا الحساب غير نشط أو محذوف، لذلك لا يمكن الموافقة على الطلب أو عكسه."),
        ["wallet.too_many_pending"] = ("You already have {max} requests waiting for approval. Please wait for them to be processed.", "لديك بالفعل {max} طلبات بانتظار الموافقة. يرجى الانتظار حتى تتم معالجتها."),
        ["account.delete_blocked"] = ("Your account cannot be deleted while it has a wallet balance, a pending withdrawal or money held by a bid. Withdraw or use the balance first.", "لا يمكن حذف حسابك بينما يوجد رصيد في المحفظة أو طلب سحب معلّق أو مبلغ محجوز لمزايدة. يرجى سحب الرصيد أو استخدامه أولاً."),
        ["wallet.conflict"] = ("This request was already submitted.", "تم إرسال هذا الطلب مسبقاً."),

        // ---- not found ----
        ["auction.not_found"] = ("This auction is no longer available.", "هذا المزاد لم يعد متاحاً."),
        ["bid.not_found"] = ("This auction is no longer available.", "هذا المزاد لم يعد متاحاً."),
        ["notification.not_found"] = ("This notification no longer exists.", "هذا الإشعار لم يعد موجوداً."),
        ["file.not_found"] = ("The file was not found.", "الملف غير موجود."),
        ["user.not_found"] = ("The account was not found.", "الحساب غير موجود."),

        // ---- by errorCode (when no specific code matched) ----
        ["UNAUTHORIZED"] = ("Your session has ended. Please sign in again.", "انتهت جلستك. يرجى تسجيل الدخول مرة أخرى."),
        ["FORBIDDEN"] = ("You are not allowed to do this.", "غير مسموح لك بتنفيذ هذا الإجراء."),
        ["NOT_FOUND"] = ("The requested item was not found.", "العنصر المطلوب غير موجود."),
        ["METHOD_NOT_ALLOWED"] = ("This action is not supported.", "هذا الإجراء غير مدعوم."),
        ["VALIDATION_FAILED"] = ("Please check the entered data.", "يرجى التحقق من البيانات المدخلة."),
        ["RATE_LIMITED"] = ("Too many attempts. Please wait a moment and try again.", "محاولات كثيرة. يرجى الانتظار قليلاً ثم المحاولة مرة أخرى."),
        ["PAYLOAD_TOO_LARGE"] = ("The upload is too large.", "حجم الملف المرفوع كبير جداً."),
        ["SERVER_ERROR"] = ("Something went wrong on our side. Please try again later.", "حدث خطأ في الخادم. يرجى المحاولة لاحقاً."),
    };

    private static readonly (string En, string Ar) Generic = ("Something went wrong. Please try again.", "حدث خطأ ما. يرجى المحاولة مرة أخرى.");

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>The text for <paramref name="code"/>, falling back to <paramref name="errorCode"/> and then a generic message.</summary>
    public static string For(string? code, string errorCode, bool arabic, object? args = null)
    {
        var fallback = Texts.TryGetValue(errorCode, out var byError) ? byError : Generic;
        var t = code is not null && Texts.TryGetValue(code, out var byCode) ? byCode : fallback;
        // A text whose {placeholder} has no value would read badly: use the general text for the errorCode instead.
        return Fill(arabic ? t.Ar : t.En, args) ?? (arabic ? fallback.Ar : fallback.En);
    }

    /// <summary>Translates a field error key (e.g. <c>email.taken</c>); text that is not a known key is returned unchanged.</summary>
    public static string Field(string key, bool arabic) => Texts.TryGetValue(key, out var t) ? (arabic ? t.Ar : t.En) : key;

    private static string? Fill(string text, object? args)
    {
        if (!text.Contains('{')) return text;
        var missing = false;
        var filled = Placeholder().Replace(text, m =>
        {
            var v = args?.GetType().GetProperty(m.Groups[1].Value)?.GetValue(args);
            if (v is null) { missing = true; return ""; }
            return v is decimal d ? d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : v.ToString() ?? "";
        });
        return missing ? null : filled;
    }
}

using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Application.Mobile;

/// <summary>Read side of the mobile API: auctions, categories and public content in the shapes of docs/API_REQUIREMENTS.md §2.</summary>
public sealed class MobileCatalog(AppDbContext db, AuctionQueries auctions, IFileUrls urls, IOptions<MobileOptions> options, TimeProvider clock, WalletService walletService, IBiddingSettings bidding, IConfiguration config)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Currency => options.Value.Currency;

    public const int StatusAll = 4, StatusActive = 1, StatusUpcoming = 2, StatusExpired = 3;

    /// <summary>Visible auctions that have not ended (live or upcoming). The public lists use this: ended auctions only show in the user's own lists (my auctions, favourites) and by id.</summary>
    private IQueryable<Auction> Running() => auctions.Visible().Where(a => a.StartsAtUtc > Now || a.EndsAtUtc > Now);

    /// <summary>List items for the given auctions (defaults to every visible auction that has not ended), newest first. Two queries: rows, then gallery images.</summary>
    public async Task<List<AuctionItemDto>> ItemsAsync(IQueryable<Auction>? source = null, CancellationToken ct = default)
    {
        var rows = await (source ?? Running()).OrderByDescending(a => a.Id).Select(a => new
        {
            a.Id, a.CategoryId, a.TitleEn, a.TitleAr, a.MainImageFileId, a.CurrentPrice, a.BidIncrement, a.StartsAtUtc, a.EndsAtUtc, a.BidCount,
        }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var images = (await db.AuctionImages.AsNoTracking().Where(i => ids.Contains(i.AuctionId))
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new { i.AuctionId, i.FileId }).ToListAsync(ct))
            .ToLookup(i => i.AuctionId, i => urls.Of(i.FileId)!);
        var now = Now;
        return rows.Select(r => new AuctionItemDto(r.Id, r.CategoryId, r.TitleEn, r.TitleAr, urls.Of(r.MainImageFileId), images[r.Id].ToList(),
            r.CurrentPrice, r.BidIncrement, r.EndsAtUtc, Currency, r.BidCount,
            AuctionRules.Phase(r.StartsAtUtc, r.EndsAtUtc, now) == AuctionPhase.Ended)).ToList();
    }

    /// <summary>Tabs in display order; the first ("All") means "no status filter".</summary>
    public async Task<List<AuctionStatusDto>> StatusesAsync(CancellationToken ct = default)
    {
        var now = Now;
        var phases = await Running().Select(a => new { a.Id, a.StartsAtUtc, a.EndsAtUtc }).ToListAsync(ct);
        var phaseOf = phases.ToDictionary(a => a.Id, a => AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, now));
        var all = await ItemsAsync(ct: ct);
        List<AuctionItemDto> In(AuctionPhase p) => all.Where(a => phaseOf.GetValueOrDefault(a.Id, AuctionPhase.Ended) == p).ToList();
        AuctionStatusDto Group(int id, string en, string ar, List<AuctionItemDto> list) => new(id, en, ar, list.Count, list);
        return
        [
            Group(StatusAll, "All", "الكل", all),
            Group(StatusActive, "Active", "نشط", In(AuctionPhase.Live)),
            Group(StatusUpcoming, "Upcoming", "قادم", In(AuctionPhase.Upcoming)),
            Group(StatusExpired, "Expired", "منتهي", []),   // the tab stays so the app keeps its layout; ended auctions are only in the user's own lists
        ];
    }

    public async Task<List<CategoryDto>> CategoriesAsync(CancellationToken ct = default)
    {
        var visible = Running();
        var rows = await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.TitleEn)
            .Select(c => new { c.Id, c.TitleEn, c.TitleAr, c.IconFileId, Count = visible.Count(a => a.CategoryId == c.Id) })
            .ToListAsync(ct);
        return rows.Select(c => new CategoryDto(c.Id, c.TitleEn, c.TitleAr, urls.Of(c.IconFileId), null, c.Count, [])).ToList();
    }

    /// <summary>Null when the auction does not exist or is not publicly visible.</summary>
    public async Task<AuctionDetailsDto?> DetailsAsync(int id, int viewerId, CancellationToken ct = default)
    {
        var a = await auctions.Visible().Include(x => x.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;

        var now = Now;
        var phase = AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, now);
        var wallet = await walletService.ShownBalanceAsync(viewerId, ct);
        // Read-only: who leads, and what the viewer's leading bid holds (same rule as the website's viewer state).
        var top = await db.Bids.AsNoTracking().Where(b => b.AuctionId == id).OrderByDescending(b => b.Amount).ThenBy(b => b.Id)
            .Select(b => new { b.UserId, b.HeldAmount, b.ReleasedAtUtc, b.SettledAtUtc }).FirstOrDefaultAsync(ct);
        var leading = top?.UserId == viewerId;
        var held = top is { ReleasedAtUtc: null, SettledAtUtc: null } ? top.HeldAmount : 0m;
        // Sale outcome, read from the columns the end job and the administrator already write (nothing here changes any of them).
        var ended = phase == AuctionPhase.Ended && top is not null;
        var voided = top is { HeldAmount: > 0, ReleasedAtUtc: not null, SettledAtUtc: null };   // void-sale or a deactivation released the winner's hold
        string? saleStatus = null;
        if (ended)
        {
            var lookback = now.AddHours(-Math.Clamp(config.GetValue("AuctionEnd:LookbackHours", 6), 1, 24 * 14));
            saleStatus = top!.SettledAtUtc is not null ? "Paid"
                : voided ? "Voided"
                : a.SettlementFailedAtUtc is not null ? "PaymentFailed"
                // Not charged and nothing will charge it: the end job ran (EndProcessedAtUtc), or it is older than the job's lookback and holds nothing (ended before wallet bidding).
                : a.EndProcessedAtUtc is not null || (held == 0 && !(a.EndsAtUtc is DateTime e && e >= lookback)) ? "NotCharged"
                : "Pending";
            // Only the holder of the winning bid sees that a payment failed, nothing was charged, or the sale was voided; everyone else sees Paid or Pending.
            if (!leading && saleStatus != "Paid") saleStatus = "Pending";
        }
        var bids = await db.Bids.AsNoTracking().Where(b => b.AuctionId == id).OrderByDescending(b => b.Id).Take(20)
            .Select(b => new LatestBidDto(b.Id, b.UserId, b.Amount, b.CreatedAtUtc)).ToListAsync(ct);

        var docIds = new[] { a.TermsFileId, a.PaymentConditionsFileId, a.ContractFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var files = await db.Files.AsNoTracking().Where(f => docIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var documents = new List<AuctionDocumentDto>();
        void AddDoc(int docId, Guid? fileId)
        {
            if (fileId is null || !files.TryGetValue(fileId.Value, out var f)) return;
            var ext = Path.GetExtension(f.OriginalName).TrimStart('.').ToLowerInvariant();
            documents.Add(new AuctionDocumentDto(docId, f.OriginalName, ext.Length > 0 ? ext : f.ContentType, (f.SizeBytes + 1023) / 1024, urls.Of(f.Id)));
        }
        AddDoc(1, a.TermsFileId);
        AddDoc(2, a.PaymentConditionsFileId);
        AddDoc(3, a.ContractFileId);

        return new AuctionDetailsDto(a.Id, a.CategoryId, a.TitleEn, a.TitleAr, urls.Of(a.MainImageFileId), a.ShortDescEn, a.ShortDescAr,
            a.DescEn, a.DescAr, a.StartPrice, a.CurrentPrice, a.BidIncrement, a.StartsAtUtc, a.EndsAtUtc, Remaining(a.EndsAtUtc, now), a.RefNumber,
            phase switch { AuctionPhase.Live => "Active", AuctionPhase.Upcoming => "Upcoming", _ => "Expired" },
            urls.Of(a.TermsFileId), a.VideoUrl, urls.Of(a.ContractFileId), null, null, a.InsuranceAmount, wallet,
            a.Images.Select(i => new AuctionImageDto(i.Id, urls.Of(i.FileId))).ToList(), a.BidCount, phase == AuctionPhase.Ended,
            Currency, bids, documents, leading, leading ? held : 0m, bidding.DebitWallet, ended && !voided && leading, saleStatus);
    }

    private static string Remaining(DateTime? end, DateTime now)
    {
        if (end is null || end <= now) return "";
        var left = end.Value - now;
        return left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes}m" : $"{Math.Max(1, left.Minutes)}m";
    }

    public async Task<List<SlideDto>> SlidesAsync(CancellationToken ct = default) =>
        (await db.Sliders.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct))
        .Select(s => new SlideDto(s.Id, urls.Of(s.ImageFileId), s.Title, s.TitleAr ?? s.Title, s.Description, s.DescriptionAr ?? s.Description,
            s.ButtonText, s.ButtonTextAr ?? s.ButtonText, s.Link)).ToList();

    public async Task<List<MobileCountryDto>> CountriesAsync(CancellationToken ct = default) =>
        (await db.Countries.AsNoTracking().Allowed().Where(c => c.IsActive).OrderBy(c => c.NameEn).ToListAsync(ct))
        .Select(c => new MobileCountryDto(c.Id, c.NameEn, c.NameAr, urls.Of(c.FlagFileId), c.Code)).ToList();

    /// <summary>Fixed ids the app looks pages up by: 1 About, 2 Terms, 3 Privacy, 4 Payment. Missing pages are left out.</summary>
    public async Task<List<ContentDto>> ContentsAsync(CancellationToken ct = default)
    {
        var list = new List<ContentDto>();
        if (await db.AboutSections.AsNoTracking().OrderBy(a => a.Id).FirstOrDefaultAsync(ct) is { } about)
            list.Add(new ContentDto(1, about.TitleEn ?? "About us", about.TitleAr ?? "من نحن", about.DescEn, about.DescAr));

        var settings = await db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.Terms || s.Key == SettingKeys.PrivacyPolicy || s.Key.StartsWith("payment")).ToDictionaryAsync(s => s.Key, ct);
        if (settings.GetValueOrDefault(SettingKeys.Terms) is { } terms)
            list.Add(new ContentDto(2, terms.TitleEn ?? "Terms & Conditions", terms.TitleAr ?? "الشروط والأحكام", terms.ValueEn, terms.ValueAr));
        if (settings.GetValueOrDefault(SettingKeys.PrivacyPolicy) is { } privacy)
            list.Add(new ContentDto(3, privacy.TitleEn ?? "Privacy Policy", privacy.TitleAr ?? "سياسة الخصوصية", privacy.ValueEn, privacy.ValueAr));
        if (settings.GetValueOrDefault(SettingKeys.Payment) is { } payment)
            list.Add(new ContentDto(4, payment.TitleEn ?? "Payment", payment.TitleAr ?? "الدفع", payment.ValueEn, payment.ValueAr));
        else if (PaymentFromBankSettings(settings) is { } built)
            list.Add(built);
        return list;
    }

    /// <summary>Without a "payment" page, the bank-transfer details (payment-* settings) become the page.</summary>
    private static ContentDto? PaymentFromBankSettings(Dictionary<string, Setting> s)
    {
        (string key, string en, string ar)[] fields =
        [
            ("payment-bank-name", "Bank", "البنك"), ("payment-account-holder", "Account holder", "اسم صاحب الحساب"), ("payment-iban", "IBAN", "IBAN"),
            ("payment-swift", "SWIFT", "SWIFT"), ("payment-cliq", "CliQ alias", "اسم CliQ"), ("payment-processing-time", "Processing time", "مدة المعالجة"),
        ];
        var present = fields.Where(f => s.TryGetValue(f.key, out var v) && !string.IsNullOrWhiteSpace(v.ValueEn ?? v.ValueAr)).ToList();
        if (present.Count == 0) return null;
        static string E(string? v) => System.Net.WebUtility.HtmlEncode(v ?? "");
        string Html(bool ar) => "<ul>" + string.Concat(present.Select(f =>
            $"<li><strong>{E(ar ? f.ar : f.en)}:</strong> {E(ar ? s[f.key].ValueAr ?? s[f.key].ValueEn : s[f.key].ValueEn ?? s[f.key].ValueAr)}</li>")) + "</ul>";
        return new ContentDto(4, "Payment", "الدفع", Html(false), Html(true));
    }

    public async Task<List<FaqItemDto>> FaqsAsync(CancellationToken ct = default) =>
        await db.Faqs.AsNoTracking().Where(f => f.IsActive && db.FaqCategories.Any(c => c.Id == f.CategoryId && c.IsActive))
            .OrderBy(f => db.FaqCategories.Where(c => c.Id == f.CategoryId).Select(c => c.SortOrder).FirstOrDefault()).ThenBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new FaqItemDto(f.Id, f.QuestionEn, f.QuestionAr, f.AnswerEn, f.AnswerAr)).ToListAsync(ct);

    public async Task<ContactUsDto> ContactUsAsync(CancellationToken ct = default)
    {
        var c = await db.ContactInfos.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return c is null ? new ContactUsDto(null, null, null, null, null, null, null, null)
            : new ContactUsDto(c.Mobile, c.Email, c.Address, c.MapUrl, c.Facebook, c.Instagram, c.Twitter, c.Linkedin);
    }
}

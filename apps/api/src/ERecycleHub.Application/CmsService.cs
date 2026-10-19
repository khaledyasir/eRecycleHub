using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record AdminCategory(int Id, int CountryId, string? CountryNameEn, string TitleEn, string TitleAr, int SortOrder, bool IsActive, Guid? IconFileId, Guid? ImageFileId, int AuctionCount);
public sealed record CategoryInput(int CountryId, string? TitleEn, string? TitleAr, int SortOrder, bool IsActive);
public sealed record AdminCountry(int Id, string NameEn, string NameAr, string? Code, bool IsActive, Guid? FlagFileId);
public sealed record CountryInput(string? NameEn, string? NameAr, string? Code, bool IsActive);
public sealed record AdminAnnouncement(int Id, int CountryId, string? CountryNameEn, Guid? ImageFileId, int SortOrder, bool IsActive);
public sealed record AnnouncementInput(int CountryId, int SortOrder, bool IsActive);
public sealed record AdminSlider(int Id, string? Title, string? Link, string Type, Guid? ImageFileId, int SortOrder, bool IsActive);
public sealed record SliderInput(string? Title, string? Link, string? Type, int SortOrder, bool IsActive);
public sealed record AboutInput(string? TitleEn, string? TitleAr, string? DescEn, string? DescAr, int? TotalBidders, string? GuidelinesVideoUrl);
public sealed record HeroInput(string? BadgeEn, string? BadgeAr, string? TitleEn, string? TitleAr, string? LeadEn, string? LeadAr,
    string? PrimaryCtaEn, string? PrimaryCtaAr, string? PrimaryCtaUrl, string? SecondaryCtaEn, string? SecondaryCtaAr, string? SecondaryCtaUrl,
    string? CardTitleEn, string? CardTitleAr, string? ImageAltEn, string? ImageAltAr, bool ShowSearch, bool IsActive);
public sealed record AdminBenefit(int Id, string TitleEn, string TitleAr, string? DescEn, string? DescAr, Guid? IconFileId, int SortOrder, bool IsActive);
public sealed record BenefitInput(string? TitleEn, string? TitleAr, string? DescEn, string? DescAr, int SortOrder, bool IsActive);
public sealed record AdminStep(int Id, string TitleEn, string TitleAr, string? DescEn, string? DescAr, int SortOrder, bool IsActive);
public sealed record StepInput(string? TitleEn, string? TitleAr, string? DescEn, string? DescAr, int SortOrder, bool IsActive);
public sealed record AdminFaqCategory(int Id, string TitleEn, string TitleAr, int SortOrder, bool IsActive, int FaqCount);
public sealed record FaqCategoryInput(string? TitleEn, string? TitleAr, int SortOrder, bool IsActive);
public sealed record AdminFaq(int Id, int CategoryId, string QuestionEn, string QuestionAr, string? AnswerEn, string? AnswerAr, int SortOrder, bool IsActive);
public sealed record FaqInput(int CategoryId, string? QuestionEn, string? QuestionAr, string? AnswerEn, string? AnswerAr, int SortOrder, bool IsActive);
public sealed record ContactInfoInput(int CountryId, string? Mobile, string? Address, string? Email, string? MapUrl, string? Facebook, string? Instagram, string? Twitter, string? Linkedin);
public sealed record AdminSetting(int Id, string Key, string? TitleEn, string? TitleAr, string? ValueEn, string? ValueAr);
public sealed record SettingInput(string? Key, string? TitleEn, string? TitleAr, string? ValueEn, string? ValueAr);
public sealed record MessageDto(int Id, string Name, string Email, string? Mobile, string Message, bool IsRead, DateTime CreatedAtUtc);
public sealed record TranslationDto(string Locale, string Key, string Value, DateTime UpdatedAtUtc);
public sealed record AuditDto(long Id, int? ActorUserId, string? ActorName, string Action, string EntityType, string? EntityId, string? Data, string? IpAddress, DateTime CreatedAtUtc);

/// <summary>Administrator CRUD for reference data and website content. Rich text is sanitised; bilingual pairs fall back to each other.</summary>
public sealed class CmsService(AppDbContext db, FileService files, TimeProvider clock, Audit audit)
{
    private static (string en, string ar) Pair(string? en, string? ar, string error)
    {
        var e = Normalize.Text(en);
        var a = Normalize.Text(ar);
        if (e is null && a is null) throw new AppException(error);
        return (e ?? a!, a ?? e!);
    }

    private static string? Url(string? value, string error)
    {
        var v = Normalize.Text(value);
        if (v is null) return null;
        if (v.StartsWith('/') && !v.StartsWith("//")) return v;
        return Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http" || u.Scheme == "mailto") ? v : throw new AppException(error);
    }

    private async Task<T> FindAsync<T>(DbSet<T> set, int id, CancellationToken ct) where T : class =>
        await set.FindAsync([id], ct) ?? throw AppException.NotFound(typeof(T).Name.ToLowerInvariant());

    /// <summary>Saves, then records the audit row (the id is read after the save so creations log their new id).</summary>
    private async Task SaveAsync(Actor admin, string action, string entity, Func<object?> id, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        audit.Record(admin, action, entity, id());
        await db.SaveChangesAsync(ct);
    }

    // ---------- categories ----------
    public Task<List<AdminCategory>> CategoriesAsync(bool? active, CancellationToken ct = default) =>
        db.Categories.AsNoTracking().Where(c => active == null || c.IsActive == active).OrderBy(c => c.SortOrder).ThenBy(c => c.TitleEn)
            .Select(c => new AdminCategory(c.Id, c.CountryId, c.Country!.NameEn, c.TitleEn, c.TitleAr, c.SortOrder, c.IsActive, c.IconFileId, c.ImageFileId,
                db.Auctions.Count(a => a.CategoryId == c.Id))).ToListAsync(ct);

    public async Task<int> SaveCategoryAsync(Actor admin, int? id, CategoryInput i, UploadInput? icon, UploadInput? image, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.TitleEn, i.TitleAr, "category.title_required");
        if (!await db.Countries.Allowed().AnyAsync(c => c.Id == i.CountryId, ct)) throw new AppException("country.invalid");
        var c = id is null ? db.Categories.Add(new Category { TitleEn = en, TitleAr = ar }).Entity : await FindAsync(db.Categories, id.Value, ct);
        if (id is not null && c.CountryId != i.CountryId && await db.Auctions.AnyAsync(a => a.CategoryId == c.Id, ct)) throw AppException.Conflict("category.in_use");
        c.TitleEn = en; c.TitleAr = ar; c.CountryId = i.CountryId; c.SortOrder = i.SortOrder; c.IsActive = i.IsActive;
        c.IconFileId = await files.SaveOptionalAsync(icon, FileCategory.Cms, admin.UserId, c.IconFileId, ct);
        c.ImageFileId = await files.SaveOptionalAsync(image, FileCategory.Cms, admin.UserId, c.ImageFileId, ct);
        await SaveAsync(admin, id is null ? "category.create" : "category.update", nameof(Category), () => c.Id, ct);
        return c.Id;
    }

    public async Task DeleteCategoryAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var c = await FindAsync(db.Categories, id, ct);
        if (await db.Auctions.AnyAsync(a => a.CategoryId == id, ct) || await db.Set<PublisherApplicationCategory>().AnyAsync(x => x.CategoryId == id, ct))
            throw AppException.Conflict("category.in_use");
        db.Categories.Remove(c);
        await SaveAsync(admin, "category.delete", nameof(Category), () => id, ct);
    }

    // ---------- countries ----------
    public Task<List<AdminCountry>> CountriesAsync(bool? active, CancellationToken ct = default) =>
        db.Countries.AsNoTracking().Allowed().Where(c => active == null || c.IsActive == active).OrderBy(c => c.NameEn)
            .Select(c => new AdminCountry(c.Id, c.NameEn, c.NameAr, c.Code, c.IsActive, c.FlagFileId)).ToListAsync(ct);

    /// <summary>Creating a country also creates its (empty) contact-info row, as the legacy admin did.</summary>
    public async Task<int> SaveCountryAsync(Actor admin, int? id, CountryInput i, UploadInput? flag, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.NameEn, i.NameAr, "country.name_required");
        var c = id is null ? db.Countries.Add(new Country { NameEn = en, NameAr = ar }).Entity : await FindAsync(db.Countries, id.Value, ct);
        c.NameEn = en; c.NameAr = ar; c.Code = Normalize.Text(i.Code)?.ToUpperInvariant(); c.IsActive = i.IsActive;
        c.FlagFileId = await files.SaveOptionalAsync(flag, FileCategory.Cms, admin.UserId, c.FlagFileId, ct);
        if (id is null) await db.SaveChangesAsync(ct);
        if (id is null) db.ContactInfos.Add(new ContactInfo { CountryId = c.Id, Address = c.NameEn });
        await SaveAsync(admin, id is null ? "country.create" : "country.update", nameof(Country), () => c.Id, ct);
        return c.Id;
    }

    public async Task DeleteCountryAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var c = await FindAsync(db.Countries, id, ct);
        if (await db.Users.AnyAsync(u => u.CountryId == id, ct) || await db.Auctions.AnyAsync(a => a.CountryId == id, ct)
            || await db.Categories.AnyAsync(x => x.CountryId == id, ct) || await db.Publishers.AnyAsync(p => p.CountryId == id, ct)
            || await db.Announcements.AnyAsync(a => a.CountryId == id, ct) || await db.PublisherApplications.AnyAsync(a => a.CountryId == id, ct))
            throw AppException.Conflict("country.in_use");
        await db.ContactInfos.Where(x => x.CountryId == id).ExecuteDeleteAsync(ct);
        db.Countries.Remove(c);
        await SaveAsync(admin, "country.delete", nameof(Country), () => id, ct);
    }

    // ---------- announcements ----------
    public Task<List<AdminAnnouncement>> AnnouncementsAsync(CancellationToken ct = default) =>
        db.Announcements.AsNoTracking().OrderBy(a => a.SortOrder)
            .Select(a => new AdminAnnouncement(a.Id, a.CountryId, db.Countries.Where(c => c.Id == a.CountryId).Select(c => c.NameEn).FirstOrDefault(), a.ImageFileId, a.SortOrder, a.IsActive)).ToListAsync(ct);

    public async Task<int> SaveAnnouncementAsync(Actor admin, int? id, AnnouncementInput i, UploadInput? image, CancellationToken ct = default)
    {
        if (!await db.Countries.Allowed().AnyAsync(c => c.Id == i.CountryId, ct)) throw new AppException("country.invalid");
        var a = id is null ? db.Announcements.Add(new Announcement()).Entity : await FindAsync(db.Announcements, id.Value, ct);
        if (id is null && image is null) throw new AppException("cms.image_required");
        a.CountryId = i.CountryId; a.SortOrder = i.SortOrder; a.IsActive = i.IsActive;
        a.ImageFileId = await files.SaveOptionalAsync(image, FileCategory.Cms, admin.UserId, a.ImageFileId, ct);
        await SaveAsync(admin, id is null ? "announcement.create" : "announcement.update", nameof(Announcement), () => a.Id, ct);
        return a.Id;
    }

    public async Task DeleteAnnouncementAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.Announcements.Remove(await FindAsync(db.Announcements, id, ct));
        await SaveAsync(admin, "announcement.delete", nameof(Announcement), () => id, ct);
    }

    // ---------- sliders ----------
    public Task<List<AdminSlider>> SlidersAsync(CancellationToken ct = default) =>
        db.Sliders.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new AdminSlider(s.Id, s.Title, s.Link, s.Type, s.ImageFileId, s.SortOrder, s.IsActive)).ToListAsync(ct);

    public async Task<int> SaveSliderAsync(Actor admin, int? id, SliderInput i, UploadInput? image, CancellationToken ct = default)
    {
        var type = i.Type is "Mobile" ? "Mobile" : "Web";
        var s = id is null ? db.Sliders.Add(new Slider()).Entity : await FindAsync(db.Sliders, id.Value, ct);
        if (id is null && image is null) throw new AppException("cms.image_required");
        s.Title = Normalize.Text(i.Title); s.Link = Url(i.Link, "cms.invalid_link"); s.Type = type; s.SortOrder = i.SortOrder; s.IsActive = i.IsActive;
        s.ImageFileId = await files.SaveOptionalAsync(image, FileCategory.Cms, admin.UserId, s.ImageFileId, ct);
        await SaveAsync(admin, id is null ? "slider.create" : "slider.update", nameof(Slider), () => s.Id, ct);
        return s.Id;
    }

    public async Task DeleteSliderAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.Sliders.Remove(await FindAsync(db.Sliders, id, ct));
        await SaveAsync(admin, "slider.delete", nameof(Slider), () => id, ct);
    }

    // ---------- about ----------
    public Task<List<AboutDto>> AboutAsync(CancellationToken ct = default) =>
        db.AboutSections.AsNoTracking().OrderBy(a => a.Id).Select(a => new AboutDto(a.Id, a.TitleEn, a.TitleAr, a.DescEn, a.DescAr, a.FirstImageFileId, a.SecondImageFileId, a.TotalBidders, a.GuidelinesVideoUrl)).ToListAsync(ct);

    public async Task<int> SaveAboutAsync(Actor admin, int? id, AboutInput i, UploadInput? first, UploadInput? second, CancellationToken ct = default)
    {
        var a = id is null ? db.AboutSections.Add(new AboutSection()).Entity : await FindAsync(db.AboutSections, id.Value, ct);
        if (i.TotalBidders is < 0) throw new AppException("cms.invalid_number");
        a.TitleEn = Normalize.Text(i.TitleEn); a.TitleAr = Normalize.Text(i.TitleAr);
        a.DescEn = RichText.Sanitize(i.DescEn); a.DescAr = RichText.Sanitize(i.DescAr);
        a.TotalBidders = i.TotalBidders; a.GuidelinesVideoUrl = Url(i.GuidelinesVideoUrl, "cms.invalid_link");
        a.FirstImageFileId = await files.SaveOptionalAsync(first, FileCategory.Cms, admin.UserId, a.FirstImageFileId, ct);
        a.SecondImageFileId = await files.SaveOptionalAsync(second, FileCategory.Cms, admin.UserId, a.SecondImageFileId, ct);
        await SaveAsync(admin, id is null ? "about.create" : "about.update", nameof(AboutSection), () => a.Id, ct);
        return a.Id;
    }

    public async Task DeleteAboutAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.AboutSections.Remove(await FindAsync(db.AboutSections, id, ct));
        await SaveAsync(admin, "about.delete", nameof(AboutSection), () => id, ct);
    }

    // ---------- homepage hero ----------
    public Task<List<HeroDto>> HeroesAsync(CancellationToken ct = default) =>
        PublicContent.HeroQuery(db.HomeHeroes.AsNoTracking()).ToListAsync(ct);

    /// <summary>Plain text (rendered as text, never as HTML); the {…}/[…] highlight markup is interpreted by the web app.</summary>
    private static string? Short(string? value, int max)
    {
        var v = Normalize.Text(value);
        return v is not null && v.Length > max ? throw new AppException("cms.too_long", 400, new { limit = max }) : v;
    }

    public async Task<int> SaveHeroAsync(Actor admin, int? id, HeroInput i, UploadInput? image, CancellationToken ct = default)
    {
        // One record only, otherwise the homepage would have to choose between them.
        if (id is null && await db.HomeHeroes.AnyAsync(ct)) throw AppException.Conflict("cms.hero_exists");
        var h = id is null ? db.HomeHeroes.Add(new HomeHero()).Entity : await FindAsync(db.HomeHeroes, id.Value, ct);
        h.BadgeEn = Short(i.BadgeEn, 120); h.BadgeAr = Short(i.BadgeAr, 120);
        h.TitleEn = Short(i.TitleEn, 300); h.TitleAr = Short(i.TitleAr, 300);
        h.LeadEn = Short(i.LeadEn, 1000); h.LeadAr = Short(i.LeadAr, 1000);
        h.PrimaryCtaEn = Short(i.PrimaryCtaEn, 120); h.PrimaryCtaAr = Short(i.PrimaryCtaAr, 120);
        h.PrimaryCtaUrl = Url(Short(i.PrimaryCtaUrl, 2000), "cms.invalid_link");
        h.SecondaryCtaEn = Short(i.SecondaryCtaEn, 120); h.SecondaryCtaAr = Short(i.SecondaryCtaAr, 120);
        h.SecondaryCtaUrl = Url(Short(i.SecondaryCtaUrl, 2000), "cms.invalid_link");
        h.CardTitleEn = Short(i.CardTitleEn, 120); h.CardTitleAr = Short(i.CardTitleAr, 120);
        h.ImageAltEn = Short(i.ImageAltEn, 300); h.ImageAltAr = Short(i.ImageAltAr, 300);
        h.ShowSearch = i.ShowSearch; h.IsActive = i.IsActive;
        h.ImageFileId = await files.SaveOptionalAsync(image, FileCategory.Cms, admin.UserId, h.ImageFileId, ct);
        h.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await SaveAsync(admin, id is null ? "hero.create" : "hero.update", nameof(HomeHero), () => h.Id, ct);
        return h.Id;
    }

    public async Task DeleteHeroAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.HomeHeroes.Remove(await FindAsync(db.HomeHeroes, id, ct));
        await SaveAsync(admin, "hero.delete", nameof(HomeHero), () => id, ct);
    }

    // ---------- benefits ----------
    public Task<List<AdminBenefit>> BenefitsAsync(CancellationToken ct = default) =>
        db.Benefits.AsNoTracking().OrderBy(b => b.SortOrder).Select(b => new AdminBenefit(b.Id, b.TitleEn, b.TitleAr, b.DescEn, b.DescAr, b.IconFileId, b.SortOrder, b.IsActive)).ToListAsync(ct);

    public async Task<int> SaveBenefitAsync(Actor admin, int? id, BenefitInput i, UploadInput? icon, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.TitleEn, i.TitleAr, "cms.title_required");
        var b = id is null ? db.Benefits.Add(new Benefit { TitleEn = en, TitleAr = ar }).Entity : await FindAsync(db.Benefits, id.Value, ct);
        b.TitleEn = en; b.TitleAr = ar; b.DescEn = RichText.Sanitize(i.DescEn); b.DescAr = RichText.Sanitize(i.DescAr); b.SortOrder = i.SortOrder; b.IsActive = i.IsActive;
        b.IconFileId = await files.SaveOptionalAsync(icon, FileCategory.Cms, admin.UserId, b.IconFileId, ct);
        await SaveAsync(admin, id is null ? "benefit.create" : "benefit.update", nameof(Benefit), () => b.Id, ct);
        return b.Id;
    }

    public async Task DeleteBenefitAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.Benefits.Remove(await FindAsync(db.Benefits, id, ct));
        await SaveAsync(admin, "benefit.delete", nameof(Benefit), () => id, ct);
    }

    // ---------- how-to steps ----------
    public Task<List<AdminStep>> StepsAsync(CancellationToken ct = default) =>
        db.HowToSteps.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new AdminStep(s.Id, s.TitleEn, s.TitleAr, s.DescEn, s.DescAr, s.SortOrder, s.IsActive)).ToListAsync(ct);

    public async Task<int> SaveStepAsync(Actor admin, int? id, StepInput i, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.TitleEn, i.TitleAr, "cms.title_required");
        var s = id is null ? db.HowToSteps.Add(new HowToStep { TitleEn = en, TitleAr = ar }).Entity : await FindAsync(db.HowToSteps, id.Value, ct);
        s.TitleEn = en; s.TitleAr = ar; s.DescEn = RichText.Sanitize(i.DescEn); s.DescAr = RichText.Sanitize(i.DescAr); s.SortOrder = i.SortOrder; s.IsActive = i.IsActive;
        await SaveAsync(admin, id is null ? "howto.create" : "howto.update", nameof(HowToStep), () => s.Id, ct);
        return s.Id;
    }

    public async Task DeleteStepAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.HowToSteps.Remove(await FindAsync(db.HowToSteps, id, ct));
        await SaveAsync(admin, "howto.delete", nameof(HowToStep), () => id, ct);
    }

    // ---------- FAQ ----------
    public Task<List<AdminFaqCategory>> FaqCategoriesAsync(CancellationToken ct = default) =>
        db.FaqCategories.AsNoTracking().OrderBy(c => c.SortOrder).Select(c => new AdminFaqCategory(c.Id, c.TitleEn, c.TitleAr, c.SortOrder, c.IsActive, c.Faqs.Count)).ToListAsync(ct);

    public async Task<int> SaveFaqCategoryAsync(Actor admin, int? id, FaqCategoryInput i, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.TitleEn, i.TitleAr, "cms.title_required");
        var c = id is null ? db.FaqCategories.Add(new FaqCategory { TitleEn = en, TitleAr = ar }).Entity : await FindAsync(db.FaqCategories, id.Value, ct);
        c.TitleEn = en; c.TitleAr = ar; c.SortOrder = i.SortOrder; c.IsActive = i.IsActive;
        await SaveAsync(admin, id is null ? "faqcategory.create" : "faqcategory.update", nameof(FaqCategory), () => c.Id, ct);
        return c.Id;
    }

    public async Task DeleteFaqCategoryAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var c = await FindAsync(db.FaqCategories, id, ct);
        if (await db.Faqs.AnyAsync(f => f.CategoryId == id, ct)) throw AppException.Conflict("faqcategory.not_empty");
        db.FaqCategories.Remove(c);
        await SaveAsync(admin, "faqcategory.delete", nameof(FaqCategory), () => id, ct);
    }

    public Task<List<AdminFaq>> FaqsAsync(int? categoryId, CancellationToken ct = default) =>
        db.Faqs.AsNoTracking().Where(f => categoryId == null || f.CategoryId == categoryId).OrderBy(f => f.SortOrder)
            .Select(f => new AdminFaq(f.Id, f.CategoryId, f.QuestionEn, f.QuestionAr, f.AnswerEn, f.AnswerAr, f.SortOrder, f.IsActive)).ToListAsync(ct);

    public async Task<int> SaveFaqAsync(Actor admin, int? id, FaqInput i, CancellationToken ct = default)
    {
        var (en, ar) = Pair(i.QuestionEn, i.QuestionAr, "faq.question_required");
        if (!await db.FaqCategories.AnyAsync(c => c.Id == i.CategoryId, ct)) throw new AppException("faqcategory.invalid");
        var f = id is null ? db.Faqs.Add(new Faq { QuestionEn = en, QuestionAr = ar }).Entity : await FindAsync(db.Faqs, id.Value, ct);
        f.CategoryId = i.CategoryId; f.QuestionEn = en; f.QuestionAr = ar; f.AnswerEn = RichText.Sanitize(i.AnswerEn); f.AnswerAr = RichText.Sanitize(i.AnswerAr);
        f.SortOrder = i.SortOrder; f.IsActive = i.IsActive;
        await SaveAsync(admin, id is null ? "faq.create" : "faq.update", nameof(Faq), () => f.Id, ct);
        return f.Id;
    }

    public async Task DeleteFaqAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.Faqs.Remove(await FindAsync(db.Faqs, id, ct));
        await SaveAsync(admin, "faq.delete", nameof(Faq), () => id, ct);
    }

    // ---------- contact info ----------
    public Task<List<ContactInfoDto>> ContactInfosAsync(CancellationToken ct = default) =>
        db.ContactInfos.AsNoTracking().OrderBy(c => c.Id).Select(c => new ContactInfoDto(c.Id, c.CountryId, c.Mobile, c.Address, c.Email, c.MapUrl, c.Facebook, c.Instagram, c.Twitter, c.Linkedin)).ToListAsync(ct);

    public async Task<int> SaveContactInfoAsync(Actor admin, int? id, ContactInfoInput i, CancellationToken ct = default)
    {
        if (!await db.Countries.Allowed().AnyAsync(c => c.Id == i.CountryId, ct)) throw new AppException("country.invalid");
        var c = id is null ? db.ContactInfos.Add(new ContactInfo()).Entity : await FindAsync(db.ContactInfos, id.Value, ct);
        c.CountryId = i.CountryId; c.Mobile = Normalize.Text(i.Mobile); c.Address = Normalize.Text(i.Address); c.Email = Normalize.Text(i.Email);
        c.MapUrl = Url(i.MapUrl, "cms.invalid_link"); c.Facebook = Url(i.Facebook, "cms.invalid_link"); c.Instagram = Url(i.Instagram, "cms.invalid_link");
        c.Twitter = Url(i.Twitter, "cms.invalid_link"); c.Linkedin = Url(i.Linkedin, "cms.invalid_link");
        await SaveAsync(admin, id is null ? "contactinfo.create" : "contactinfo.update", nameof(ContactInfo), () => c.Id, ct);
        return c.Id;
    }

    public async Task DeleteContactInfoAsync(Actor admin, int id, CancellationToken ct = default)
    {
        db.ContactInfos.Remove(await FindAsync(db.ContactInfos, id, ct));
        await SaveAsync(admin, "contactinfo.delete", nameof(ContactInfo), () => id, ct);
    }

    // ---------- settings ----------
    public Task<List<AdminSetting>> SettingsAsync(CancellationToken ct = default) =>
        db.Settings.AsNoTracking().OrderBy(s => s.Key).Select(s => new AdminSetting(s.Id, s.Key, s.TitleEn, s.TitleAr, s.ValueEn, s.ValueAr)).ToListAsync(ct);

    public async Task<int> SaveSettingAsync(Actor admin, int? id, SettingInput i, CancellationToken ct = default)
    {
        var key = Normalize.Text(i.Key)?.ToLowerInvariant();
        if (key is null || key.Length > 100 || !key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')) throw new AppException("setting.invalid_key");
        if (await db.Settings.AnyAsync(s => s.Key == key && s.Id != (id ?? 0), ct)) throw AppException.Conflict("setting.key_taken");
        var s = id is null ? db.Settings.Add(new Setting { Key = key }).Entity : await FindAsync(db.Settings, id.Value, ct);
        s.Key = key; s.TitleEn = Normalize.Text(i.TitleEn); s.TitleAr = Normalize.Text(i.TitleAr); s.ValueEn = RichText.Sanitize(i.ValueEn); s.ValueAr = RichText.Sanitize(i.ValueAr);
        await SaveAsync(admin, id is null ? "setting.create" : "setting.update", nameof(Setting), () => s.Id, ct);
        return s.Id;
    }

    public async Task DeleteSettingAsync(Actor admin, int id, CancellationToken ct = default)
    {
        var s = await FindAsync(db.Settings, id, ct);
        if (s.Key is SettingKeys.Terms or SettingKeys.PrivacyPolicy) throw AppException.Conflict("setting.required");
        db.Settings.Remove(s);
        await SaveAsync(admin, "setting.delete", nameof(Setting), () => id, ct);
    }

    // ---------- contact messages ----------
    public async Task<PagedResult<MessageDto>> MessagesAsync(bool? unread, PageQuery page, CancellationToken ct = default)
    {
        var q = db.ContactMessages.AsNoTracking().Where(m => unread == null || m.IsRead != unread);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.Id).Skip(page.Skip).Take(page.SafeSize)
            .Select(m => new MessageDto(m.Id, m.Name, m.Email, m.Mobile, m.Message, m.IsRead, m.CreatedAtUtc)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task MarkMessageReadAsync(int id, CancellationToken ct = default)
    {
        if (await db.ContactMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct) == 0) throw AppException.NotFound("message");
    }

    public async Task DeleteMessageAsync(Actor admin, int id, CancellationToken ct = default)
    {
        if (await db.ContactMessages.Where(m => m.Id == id).ExecuteDeleteAsync(ct) == 0) throw AppException.NotFound("message");
        audit.Record(admin, "message.delete", nameof(ContactMessage), id);
        await db.SaveChangesAsync(ct);
    }

    // ---------- translations ----------
    public static readonly string[] Locales = ["en", "ar"];

    public Task<List<TranslationDto>> TranslationsAsync(string? locale, CancellationToken ct = default) =>
        db.TranslationOverrides.AsNoTracking().Where(t => locale == null || t.Locale == locale).OrderBy(t => t.Key)
            .Select(t => new TranslationDto(t.Locale, t.Key, t.Value, t.UpdatedAtUtc)).ToListAsync(ct);

    public async Task SaveTranslationAsync(Actor admin, string locale, string key, string value, CancellationToken ct = default)
    {
        if (!Locales.Contains(locale)) throw new AppException("translation.invalid_locale");
        key = key.Trim();
        if (key.Length is 0 or > 200 || !key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')) throw new AppException("translation.invalid_key");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4000) throw new AppException("translation.invalid_value");
        var existing = await db.TranslationOverrides.FindAsync([locale, key], ct);
        if (existing is null) db.TranslationOverrides.Add(new TranslationOverride { Locale = locale, Key = key, Value = value, UpdatedAtUtc = clock.GetUtcNow().UtcDateTime });
        else { existing.Value = value; existing.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; }
        await SaveAsync(admin, "translation.save", nameof(TranslationOverride), () => $"{locale}:{key}", ct);
    }

    public async Task DeleteTranslationAsync(Actor admin, string locale, string key, CancellationToken ct = default)
    {
        if (await db.TranslationOverrides.Where(t => t.Locale == locale && t.Key == key).ExecuteDeleteAsync(ct) == 0) throw AppException.NotFound("translation");
        audit.Record(admin, "translation.delete", nameof(TranslationOverride), $"{locale}:{key}");
        await db.SaveChangesAsync(ct);
    }

    // ---------- audit ----------
    public async Task<PagedResult<AuditDto>> AuditAsync(string? action, string? entityType, PageQuery page, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (Normalize.Text(action) is { } a) q = q.Where(x => x.Action.StartsWith(a));
        if (Normalize.Text(entityType) is { } e) q = q.Where(x => x.EntityType == e);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id).Skip(page.Skip).Take(page.SafeSize).Select(x => new AuditDto(x.Id, x.ActorUserId,
            db.Users.Where(u => u.Id == x.ActorUserId).Select(u => u.FullName).FirstOrDefault(), x.Action, x.EntityType, x.EntityId, x.Data, x.IpAddress, x.CreatedAtUtc)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }
}

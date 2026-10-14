using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record CountryDto(int Id, string NameEn, string NameAr, string? Code, Guid? FlagFileId);
public sealed record SliderDto(int Id, string? Title, string? Link, Guid? ImageFileId);
public sealed record AnnouncementDto(int Id, Guid? ImageFileId);
public sealed record StepDto(int Id, string TitleEn, string TitleAr, string? DescEn, string? DescAr);
public sealed record StoreCard(int Id, string Name, Guid? LogoFileId, Guid? BackgroundFileId, string? Address, string? Phone, string? SecondMobile, int AuctionCount, int LiveCount);
public sealed record StoreDetails(StoreCard Store, string? Email, string? CountryNameEn, string? CountryNameAr, double RatingAverage, int RatingCount);
public sealed record HomeStats(int LiveAuctions, int Stores, int TotalBids);
public sealed record HomeDto(IReadOnlyList<SliderDto> Sliders, IReadOnlyList<AnnouncementDto> Announcements, IReadOnlyList<CategoryFacet> Categories,
    IReadOnlyList<AuctionCard> Live, IReadOnlyList<AuctionCard> Upcoming, IReadOnlyList<AuctionCard> Ended, IReadOnlyList<AuctionCard> Highest,
    IReadOnlyList<StoreCard> Stores, IReadOnlyList<StepDto> Steps, HomeStats Stats);
public sealed record ClientDto(int Id, string? CompanyName, Guid? AvatarFileId);
public sealed record AboutDto(int Id, string? TitleEn, string? TitleAr, string? DescEn, string? DescAr, Guid? FirstImageFileId, Guid? SecondImageFileId, int? TotalBidders, string? GuidelinesVideoUrl);
public sealed record BenefitDto(int Id, string TitleEn, string TitleAr, string? DescEn, string? DescAr, Guid? IconFileId);
public sealed record FaqDto(int Id, string QuestionEn, string QuestionAr, string? AnswerEn, string? AnswerAr);
public sealed record FaqCategoryDto(int Id, string TitleEn, string TitleAr, IReadOnlyList<FaqDto> Faqs);
public sealed record ContactInfoDto(int Id, int CountryId, string? Mobile, string? Address, string? Email, string? MapUrl, string? Facebook, string? Instagram, string? Twitter, string? Linkedin);
public sealed record SettingDto(string Key, string? TitleEn, string? TitleAr, string? ValueEn, string? ValueAr);
public sealed record HowToDto(IReadOnlyList<StepDto> Steps, string? GuidelinesVideoUrl);
public sealed record ContactInput(string Name, string Email, string? Mobile, string Message);
public sealed record ApplicationInput(string Name, string Email, string? Mobile, string? Company, string Password, int? CountryId, int[]? CategoryIds);

public sealed class PublicContent(AppDbContext db, AuctionQueries auctions, PasswordService passwords, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<List<CountryDto>> CountriesAsync(CancellationToken ct = default) =>
        db.Countries.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.NameEn)
            .Select(c => new CountryDto(c.Id, c.NameEn, c.NameAr, c.Code, c.FlagFileId)).ToListAsync(ct);

    public Task<List<CategoryRef>> CategoriesAsync(int? countryId, CancellationToken ct = default) =>
        db.Categories.AsNoTracking().Where(c => c.IsActive && (countryId == null || c.CountryId == countryId))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.TitleEn).Select(c => new CategoryRef(c.Id, c.TitleEn, c.TitleAr)).ToListAsync(ct);

    private IQueryable<Publisher> VisibleStores(int? countryId)
    {
        var q = db.Publishers.AsNoTracking().Where(p => p.User!.IsActive && !p.User.IsDeleted);
        return countryId is > 0 ? q.Where(p => p.CountryId == countryId) : q;
    }

    /// <summary>Projects store cards; <paramref name="byActivity"/> orders by live then total auctions (computed in SQL before the record projection).</summary>
    private IQueryable<StoreCard> StoreCards(IQueryable<Publisher> q, bool byActivity = false)
    {
        var now = Now;
        var visible = auctions.Visible();
        var rows = q.Select(p => new
        {
            p.Id, Name = p.FirstName + (p.LastName == "" ? "" : " " + p.LastName), p.LogoFileId, p.BackgroundFileId, p.Address, p.User!.Phone, p.SecondMobile,
            Total = visible.Count(a => a.PublisherId == p.Id),
            Live = visible.Count(a => a.PublisherId == p.Id && a.StartsAtUtc <= now && a.EndsAtUtc > now),
        });
        if (byActivity) rows = rows.OrderByDescending(r => r.Live).ThenByDescending(r => r.Total).ThenBy(r => r.Id);
        return rows.Select(r => new StoreCard(r.Id, r.Name, r.LogoFileId, r.BackgroundFileId, r.Address, r.Phone, r.SecondMobile, r.Total, r.Live));
    }

    public async Task<HomeDto> HomeAsync(int? countryId, int? viewerId, CancellationToken ct = default)
    {
        var now = Now;
        var q = auctions.Visible();
        if (countryId is > 0) q = q.Where(a => a.CountryId == countryId);

        var sliders = await db.Sliders.AsNoTracking().Where(s => s.IsActive && s.Type == "Web").OrderBy(s => s.SortOrder)
            .Select(s => new SliderDto(s.Id, s.Title, s.Link, s.ImageFileId)).ToListAsync(ct);
        var ann = db.Announcements.AsNoTracking().Where(a => a.IsActive);
        if (countryId is > 0) ann = ann.Where(a => a.CountryId == countryId);
        var announcements = await ann.OrderBy(a => a.SortOrder).Select(a => new AnnouncementDto(a.Id, a.ImageFileId)).ToListAsync(ct);
        var facets = await auctions.FacetsAsync(countryId, null, ct);

        var live = await auctions.CardsAsync(AuctionQueries.InPhase(q, "live", now).OrderBy(a => a.EndsAtUtc).Take(8), viewerId, ct);
        var upcoming = await auctions.CardsAsync(AuctionQueries.InPhase(q, "upcoming", now).OrderBy(a => a.StartsAtUtc).Take(8), viewerId, ct);
        var ended = await auctions.CardsAsync(AuctionQueries.InPhase(q, "ended", now).OrderByDescending(a => a.EndsAtUtc).Take(4), viewerId, ct);
        var highest = await auctions.CardsAsync(AuctionQueries.InPhase(q, "live", now).OrderByDescending(a => a.CurrentPrice).Take(3), viewerId, ct);
        var stores = await StoreCards(VisibleStores(countryId), byActivity: true).Take(6).ToListAsync(ct);
        var steps = await db.HowToSteps.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder)
            .Select(s => new StepDto(s.Id, s.TitleEn, s.TitleAr, s.DescEn, s.DescAr)).ToListAsync(ct);
        var stats = new HomeStats(
            await AuctionQueries.InPhase(q, "live", now).CountAsync(ct),
            await VisibleStores(countryId).CountAsync(ct),
            await db.Bids.CountAsync(b => q.Any(a => a.Id == b.AuctionId), ct));

        return new HomeDto(sliders, announcements, facets.Categories.Take(8).ToList(), live, upcoming, ended, highest, stores, steps, stats);
    }

    public async Task<PagedResult<StoreCard>> StoresAsync(int? countryId, string? q, PageQuery page, CancellationToken ct = default)
    {
        var stores = VisibleStores(countryId);
        if (Normalize.Text(q) is { } term) stores = stores.Where(p => p.FirstName.Contains(term) || p.LastName.Contains(term) || (p.User!.CompanyName != null && p.User.CompanyName.Contains(term)));
        var total = await stores.CountAsync(ct);
        var items = await StoreCards(stores.OrderBy(p => p.FirstName).ThenBy(p => p.Id)).Skip(page.Skip).Take(page.SafeSize).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public async Task<StoreDetails?> StoreAsync(int id, CancellationToken ct = default)
    {
        var card = await StoreCards(VisibleStores(null).Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        if (card is null) return null;
        var extra = await db.Publishers.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.User!.Email, p.Country!.NameEn, p.Country.NameAr }).SingleAsync(ct);
        var rating = await db.Reviews.Where(r => r.Auction!.PublisherId == id).GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() }).FirstOrDefaultAsync(ct);
        return new StoreDetails(card, extra.Email, extra.NameEn, extra.NameAr, Math.Round(rating?.Avg ?? 0, 1), rating?.Count ?? 0);
    }

    public async Task<PagedResult<ClientDto>> ClientsAsync(PageQuery page, CancellationToken ct = default)
    {
        var q = db.Users.AsNoTracking().Where(u => u.IsFeaturedClient && u.IsActive && !u.IsDeleted && u.AvatarFileId != null);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(u => u.Id).Skip(page.Skip).Take(page.SafeSize).Select(u => new ClientDto(u.Id, u.CompanyName, u.AvatarFileId)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    public Task<List<AboutDto>> AboutAsync(CancellationToken ct = default) =>
        db.AboutSections.AsNoTracking().OrderBy(a => a.Id).Select(a => new AboutDto(a.Id, a.TitleEn, a.TitleAr, a.DescEn, a.DescAr, a.FirstImageFileId,
            a.SecondImageFileId, a.TotalBidders, a.GuidelinesVideoUrl)).ToListAsync(ct);

    public Task<List<BenefitDto>> BenefitsAsync(CancellationToken ct = default) =>
        db.Benefits.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.SortOrder)
            .Select(b => new BenefitDto(b.Id, b.TitleEn, b.TitleAr, b.DescEn, b.DescAr, b.IconFileId)).ToListAsync(ct);

    public async Task<HowToDto> HowToAsync(CancellationToken ct = default) => new(
        await db.HowToSteps.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).Select(s => new StepDto(s.Id, s.TitleEn, s.TitleAr, s.DescEn, s.DescAr)).ToListAsync(ct),
        await db.AboutSections.AsNoTracking().OrderBy(a => a.Id).Select(a => a.GuidelinesVideoUrl).FirstOrDefaultAsync(ct));

    public async Task<List<FaqCategoryDto>> FaqAsync(CancellationToken ct = default) =>
        (await db.FaqCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.TitleEn, c.TitleAr, Faqs = c.Faqs.Where(f => f.IsActive).OrderBy(f => f.SortOrder).Select(f => new FaqDto(f.Id, f.QuestionEn, f.QuestionAr, f.AnswerEn, f.AnswerAr)).ToList() })
            .ToListAsync(ct))
        .Where(c => c.Faqs.Count > 0).Select(c => new FaqCategoryDto(c.Id, c.TitleEn, c.TitleAr, c.Faqs)).ToList();

    /// <summary>Contact details for the visitor's country, falling back to the first row (legacy behaviour).</summary>
    public async Task<ContactInfoDto?> ContactInfoAsync(int? countryId, CancellationToken ct = default)
    {
        var all = db.ContactInfos.AsNoTracking().OrderBy(c => c.Id);
        var row = (countryId is > 0 ? await all.FirstOrDefaultAsync(c => c.CountryId == countryId, ct) : null) ?? await all.FirstOrDefaultAsync(ct);
        return row is null ? null : new ContactInfoDto(row.Id, row.CountryId, row.Mobile, row.Address, row.Email, row.MapUrl, row.Facebook, row.Instagram, row.Twitter, row.Linkedin);
    }

    public Task<SettingDto?> SettingAsync(string key, CancellationToken ct = default) =>
        db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => new SettingDto(s.Key, s.TitleEn, s.TitleAr, s.ValueEn, s.ValueAr)).FirstOrDefaultAsync(ct);

    public async Task SubmitContactAsync(ContactInput input, CancellationToken ct = default)
    {
        var name = Normalize.Text(input.Name);
        var email = Normalize.Text(input.Email);
        var message = Normalize.Text(input.Message);
        if (name is null || name.Length > 200) throw new AppException("contact.name_required");
        if (email is null || !email.Contains('@') || email.Length > 256) throw new AppException("contact.email_invalid");
        if (message is null || message.Length > 4000) throw new AppException("contact.message_required");
        db.ContactMessages.Add(new ContactMessage { Name = name, Email = email, Mobile = Normalize.Text(input.Mobile), Message = message, CreatedAtUtc = Now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Publisher application ("Subscribe"). The chosen password is hashed immediately and only used if an admin approves.</summary>
    public async Task<int> SubmitApplicationAsync(ApplicationInput input, CancellationToken ct = default)
    {
        AuthService.ValidatePassword(input.Password);
        var name = Normalize.Text(input.Name) ?? throw new AppException("application.name_required");
        var email = Normalize.Email(input.Email ?? "");
        if (!email.Contains('@') || email.Length > 256) throw new AppException("application.email_invalid");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw AppException.Conflict("auth.email_taken");
        if (await db.PublisherApplications.AnyAsync(a => a.Email == email && a.Status == ApplicationStatus.Pending, ct)) throw AppException.Conflict("application.already_pending");
        if (input.CountryId is not null && !await db.Countries.AnyAsync(c => c.Id == input.CountryId && c.IsActive, ct)) throw new AppException("country.invalid");
        var categoryIds = (input.CategoryIds ?? []).Distinct().ToArray();
        if (categoryIds.Length > 0 && await db.Categories.CountAsync(c => categoryIds.Contains(c.Id) && c.IsActive, ct) != categoryIds.Length)
            throw new AppException("category.invalid");

        var app = new PublisherApplication
        {
            Name = name, Email = email, Mobile = Normalize.Text(input.Mobile), Company = Normalize.Text(input.Company), CountryId = input.CountryId,
            PasswordHash = passwords.Hash(input.Password), Status = ApplicationStatus.Pending, CreatedAtUtc = Now,
            Categories = categoryIds.Select(id => new PublisherApplicationCategory { CategoryId = id }).ToList(),
        };
        db.PublisherApplications.Add(app);
        await db.SaveChangesAsync(ct);
        return app.Id;
    }

    public Task<Dictionary<string, string>> TranslationOverridesAsync(string locale, CancellationToken ct = default) =>
        db.TranslationOverrides.AsNoTracking().Where(t => t.Locale == locale).ToDictionaryAsync(t => t.Key, t => t.Value, ct);

    public async Task<(List<int> auctions, List<int> stores)> SitemapAsync(CancellationToken ct = default) => (
        await auctions.Visible().OrderByDescending(a => a.Id).Take(5000).Select(a => a.Id).ToListAsync(ct),
        await VisibleStores(null).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct));
}

using System.Text;
using ERecycleHub.Application;
using ERecycleHub.Domain;

namespace ERecycleHub.Api.Endpoints;

public sealed record CreateUserRequest(AccountFieldsInput Account, decimal OpeningCredit = 0);
public sealed record CreditRequest(int UserId, decimal Amount, int? AuctionId, string? Note);
public sealed record FeaturedRequest(bool Featured);
public sealed record TranslationRequest(string Locale, string Key, string Value);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);
        g.MapManagedAuctions();

        // ---- dashboard, reports, winners ----
        g.MapGet("/dashboard", (HttpContext ctx, ReportsService r, CancellationToken ct) => r.DashboardAsync(ctx.Actor(), ct));
        g.MapGet("/reports/auctions", async (DateTime? from, DateTime? to, int? countryId, int? page, string? format, ReportsService r, CancellationToken ct) =>
        {
            var filter = new ReportFilter(from.AsUtc(), to.AsUtc(), countryId, page ?? 1);
            if (format == "csv")
                return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(await r.ReportCsvAsync(filter, ct))).ToArray(), "text/csv; charset=utf-8", "auctions-report.csv");
            return Results.Ok(await r.ReportAsync(filter, ct));
        }).Produces<ReportResult>();
        g.MapGet("/winners", (DateTime? from, DateTime? to, string? q, bool? notified, int? page, ReportsService r, CancellationToken ct) =>
            r.WinnersAsync(from.AsUtc(), to.AsUtc(), q, notified, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/winners/by-bidder", (DateTime? from, DateTime? to, ReportsService r, CancellationToken ct) => r.WinnersByBidderAsync(from.AsUtc(), to.AsUtc(), ct));
        g.MapPost("/winners/notify", (HttpContext ctx, ReportsService r, CancellationToken ct) =>
            r.NotifyWinnersAsync(ctx.Actor(), null, AuctionUrl(ctx), ctx.Request.IsArabic(), ct));
        g.MapPost("/auctions/{id:int}/notify-winner", (int id, HttpContext ctx, ReportsService r, CancellationToken ct) =>
            r.NotifyWinnersAsync(ctx.Actor(), id, AuctionUrl(ctx), ctx.Request.IsArabic(), ct));

        // ---- customers ----
        g.MapGet("/users", (bool? active, string? q, int? page, AdminPeople p, CancellationToken ct) => p.UsersAsync(active ?? true, q, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/users/search", (string? q, AdminPeople p, CancellationToken ct) => p.SearchAccountsAsync(q, ct));
        g.MapGet("/users/{id:int}", (int id, AdminPeople p, CancellationToken ct) => p.UserAsync(id, ct));
        g.MapPost("/users", async (HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        {
            var (data, f) = await ctx.Request.ReadMultipartAsync<CreateUserRequest>(ct);
            return new IdResponse(await p.CreateUserAsync(ctx.Actor(), data.Account, KycFiles(f), data.OpeningCredit, ct));
        }).Accepts<CreateUserRequest>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/users/{id:int}", async (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        {
            var (data, f) = await ctx.Request.ReadMultipartAsync<AccountFieldsInput>(ct);
            await p.UpdateUserAsync(ctx.Actor(), id, data, KycFiles(f), ct);
            return Results.NoContent();
        }).Accepts<AccountFieldsInput>("multipart/form-data");
        g.MapPost("/users/{id:int}/active", async (int id, ActiveRequest req, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.SetUserActiveAsync(ctx.Actor(), id, req.Active, ct); return Results.NoContent(); });
        g.MapPost("/users/{id:int}/featured", async (int id, FeaturedRequest req, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.SetFeaturedAsync(ctx.Actor(), id, req.Featured, ct); return Results.NoContent(); });
        g.MapGet("/users/{id:int}/transactions", (int id, int? page, WalletService w, CancellationToken ct) => w.ListAsync(id, null, null, null, null, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/users/{id:int}/ledger", (int id, int? page, WalletService w, CancellationToken ct) => w.LedgerAsync(id, new PageQuery(page ?? 1, 20), ct));

        // ---- publishers ----
        g.MapGet("/publishers", (bool? active, string? q, int? page, AdminPeople p, CancellationToken ct) => p.PublishersAsync(active ?? true, q, new PageQuery(page ?? 1, 20), ct));
        g.MapGet("/publishers/options", (AdminPeople p, CancellationToken ct) => p.PublisherOptionsAsync(ct));
        g.MapGet("/publishers/{id:int}", (int id, AdminPeople p, SellerService s, CancellationToken ct) => p.PublisherAsync(id, s, ct));
        g.MapPost("/publishers", async (HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        {
            var (data, f) = await ctx.Request.ReadMultipartAsync<PublisherInput>(ct);
            return new IdResponse(await p.CreatePublisherAsync(ctx.Actor(), data, PublisherFiles(f), ct));
        }).Accepts<PublisherInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/publishers/{id:int}", async (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        {
            var (data, f) = await ctx.Request.ReadMultipartAsync<PublisherInput>(ct);
            await p.UpdatePublisherAsync(ctx.Actor(), id, data, PublisherFiles(f), ct);
            return Results.NoContent();
        }).Accepts<PublisherInput>("multipart/form-data");
        g.MapPost("/publishers/{id:int}/active", async (int id, ActiveRequest req, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.SetPublisherActiveAsync(ctx.Actor(), id, req.Active, ct); return Results.NoContent(); });
        g.MapDelete("/publishers/{id:int}", async (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.DeletePublisherAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        // ---- applications ----
        g.MapGet("/applications", (ApplicationStatus? status, int? page, AdminPeople p, CancellationToken ct) => p.ApplicationsAsync(status, new PageQuery(page ?? 1, 20), ct));
        g.MapPost("/applications/{id:int}/approve", (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
            p.ApproveAsync(ctx.Actor(), id, $"{ctx.PublicUrl()}/{(ctx.Request.IsArabic() ? "ar" : "en")}/login", ctx.Request.IsArabic(), ct));
        g.MapPost("/applications/{id:int}/reject", async (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.RejectAsync(ctx.Actor(), id, ctx.Request.IsArabic(), ct); return Results.NoContent(); });
        g.MapDelete("/applications/{id:int}", async (int id, HttpContext ctx, AdminPeople p, CancellationToken ct) =>
        { await p.DeleteApplicationAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        // ---- wallet transactions ----
        g.MapGet("/transactions", (WalletTransactionStatus? status, string? q, DateTime? from, DateTime? to, int? userId, int? page, WalletService w, CancellationToken ct) =>
            w.ListAsync(userId, status, q, from.AsUtc(), to.AsUtc(), new PageQuery(page ?? 1, 20), ct));
        g.MapPost("/transactions", async (HttpContext ctx, WalletService w, FileService files, ERecycleHub.Infrastructure.AppDbContext db, CancellationToken ct) =>
        {
            var (data, f) = await ctx.Request.ReadMultipartAsync<CreditRequest>(ct);
            var proof = await SaveProofAsync(f, ctx, files, db, data.UserId, ct);
            return await w.CreateApprovedCreditAsync(ctx.Actor(), data.UserId, data.Amount, proof, data.AuctionId, data.Note, ct);
        }).Accepts<CreditRequest>("multipart/form-data").Produces<WalletTransactionDto>();
        g.MapPost("/transactions/{id:int}/approve", (int id, HttpContext ctx, WalletService w, CancellationToken ct) => w.ApproveAsync(ctx.Actor(), id, ct));
        g.MapPost("/transactions/{id:int}/reject", (int id, HttpContext ctx, WalletService w, CancellationToken ct) => w.RejectAsync(ctx.Actor(), id, ct));
        g.MapPost("/transactions/{id:int}/reverse", (int id, HttpContext ctx, WalletService w, CancellationToken ct) => w.ReverseAsync(ctx.Actor(), id, ct));
        g.MapDelete("/transactions/{id:int}", async (int id, HttpContext ctx, WalletService w, CancellationToken ct) => { await w.DeleteAsync(ctx.Actor(), id, ct); return Results.NoContent(); });
        g.MapPut("/transactions/{id:int}/proof", async (int id, HttpContext ctx, WalletService w, FileService files, ERecycleHub.Infrastructure.AppDbContext db, CancellationToken ct) =>
        {
            var owner = (await w.GetAsync(id, ct))?.UserId ?? throw AppException.NotFound("transaction");
            var form = ctx.Request.HasFormContentType ? (await ctx.Request.ReadFormAsync(ct)).Files : null;
            var proof = form is null ? null : await SaveProofAsync(form, ctx, files, db, owner, ct);
            await w.SetProofAsync(ctx.Actor(), id, proof, ct); // no file = remove the proof
            return Results.NoContent();
        });

        // ---- CMS ----
        g.MapGet("/categories", (bool? active, CmsService c, CancellationToken ct) => c.CategoriesAsync(active, ct));
        g.MapPost("/categories", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<CategoryInput>(ct); return new IdResponse(await c.SaveCategoryAsync(ctx.Actor(), null, d, f.Upload("icon"), f.Upload("image"), ct)); })
            .Accepts<CategoryInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/categories/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<CategoryInput>(ct); return new IdResponse(await c.SaveCategoryAsync(ctx.Actor(), id, d, f.Upload("icon"), f.Upload("image"), ct)); })
            .Accepts<CategoryInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/categories/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteCategoryAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/countries", (bool? active, CmsService c, CancellationToken ct) => c.CountriesAsync(active, ct));
        g.MapPost("/countries", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<CountryInput>(ct); return new IdResponse(await c.SaveCountryAsync(ctx.Actor(), null, d, f.Upload("flag"), ct)); })
            .Accepts<CountryInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/countries/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<CountryInput>(ct); return new IdResponse(await c.SaveCountryAsync(ctx.Actor(), id, d, f.Upload("flag"), ct)); })
            .Accepts<CountryInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/countries/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteCountryAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/announcements", (CmsService c, CancellationToken ct) => c.AnnouncementsAsync(ct));
        g.MapPost("/announcements", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<AnnouncementInput>(ct); return new IdResponse(await c.SaveAnnouncementAsync(ctx.Actor(), null, d, f.Upload("image"), ct)); })
            .Accepts<AnnouncementInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/announcements/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<AnnouncementInput>(ct); return new IdResponse(await c.SaveAnnouncementAsync(ctx.Actor(), id, d, f.Upload("image"), ct)); })
            .Accepts<AnnouncementInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/announcements/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteAnnouncementAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/sliders", (CmsService c, CancellationToken ct) => c.SlidersAsync(ct));
        g.MapPost("/sliders", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<SliderInput>(ct); return new IdResponse(await c.SaveSliderAsync(ctx.Actor(), null, d, f.Upload("image"), ct)); })
            .Accepts<SliderInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/sliders/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<SliderInput>(ct); return new IdResponse(await c.SaveSliderAsync(ctx.Actor(), id, d, f.Upload("image"), ct)); })
            .Accepts<SliderInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/sliders/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteSliderAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/hero", (CmsService c, CancellationToken ct) => c.HeroesAsync(ct));
        g.MapPost("/hero", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<HeroInput>(ct); return new IdResponse(await c.SaveHeroAsync(ctx.Actor(), null, d, f.Upload("image"), ct)); })
            .Accepts<HeroInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/hero/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<HeroInput>(ct); return new IdResponse(await c.SaveHeroAsync(ctx.Actor(), id, d, f.Upload("image"), ct)); })
            .Accepts<HeroInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/hero/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteHeroAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/about", (CmsService c, CancellationToken ct) => c.AboutAsync(ct));
        g.MapPost("/about", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<AboutInput>(ct); return new IdResponse(await c.SaveAboutAsync(ctx.Actor(), null, d, f.Upload("firstImage"), f.Upload("secondImage"), ct)); })
            .Accepts<AboutInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/about/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<AboutInput>(ct); return new IdResponse(await c.SaveAboutAsync(ctx.Actor(), id, d, f.Upload("firstImage"), f.Upload("secondImage"), ct)); })
            .Accepts<AboutInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/about/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteAboutAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/benefits", (CmsService c, CancellationToken ct) => c.BenefitsAsync(ct));
        g.MapPost("/benefits", async (HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<BenefitInput>(ct); return new IdResponse(await c.SaveBenefitAsync(ctx.Actor(), null, d, f.Upload("icon"), ct)); })
            .Accepts<BenefitInput>("multipart/form-data").Produces<IdResponse>();
        g.MapPut("/benefits/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) =>
        { var (d, f) = await ctx.Request.ReadMultipartAsync<BenefitInput>(ct); return new IdResponse(await c.SaveBenefitAsync(ctx.Actor(), id, d, f.Upload("icon"), ct)); })
            .Accepts<BenefitInput>("multipart/form-data").Produces<IdResponse>();
        g.MapDelete("/benefits/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteBenefitAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/how-to", (CmsService c, CancellationToken ct) => c.StepsAsync(ct));
        g.MapPost("/how-to", async (StepInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveStepAsync(ctx.Actor(), null, d, ct)));
        g.MapPut("/how-to/{id:int}", async (int id, StepInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveStepAsync(ctx.Actor(), id, d, ct)));
        g.MapDelete("/how-to/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteStepAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/faq-categories", (CmsService c, CancellationToken ct) => c.FaqCategoriesAsync(ct));
        g.MapPost("/faq-categories", async (FaqCategoryInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveFaqCategoryAsync(ctx.Actor(), null, d, ct)));
        g.MapPut("/faq-categories/{id:int}", async (int id, FaqCategoryInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveFaqCategoryAsync(ctx.Actor(), id, d, ct)));
        g.MapDelete("/faq-categories/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteFaqCategoryAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/faqs", (int? categoryId, CmsService c, CancellationToken ct) => c.FaqsAsync(categoryId, ct));
        g.MapPost("/faqs", async (FaqInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveFaqAsync(ctx.Actor(), null, d, ct)));
        g.MapPut("/faqs/{id:int}", async (int id, FaqInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveFaqAsync(ctx.Actor(), id, d, ct)));
        g.MapDelete("/faqs/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteFaqAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/contact-info", (CmsService c, CancellationToken ct) => c.ContactInfosAsync(ct));
        g.MapPost("/contact-info", async (ContactInfoInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveContactInfoAsync(ctx.Actor(), null, d, ct)));
        g.MapPut("/contact-info/{id:int}", async (int id, ContactInfoInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveContactInfoAsync(ctx.Actor(), id, d, ct)));
        g.MapDelete("/contact-info/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteContactInfoAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/settings", (CmsService c, CancellationToken ct) => c.SettingsAsync(ct));
        g.MapPost("/settings", async (SettingInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveSettingAsync(ctx.Actor(), null, d, ct)));
        g.MapPut("/settings/{id:int}", async (int id, SettingInput d, HttpContext ctx, CmsService c, CancellationToken ct) => new IdResponse(await c.SaveSettingAsync(ctx.Actor(), id, d, ct)));
        g.MapDelete("/settings/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteSettingAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/messages", (bool? unread, int? page, CmsService c, CancellationToken ct) => c.MessagesAsync(unread, new PageQuery(page ?? 1, 20), ct));
        g.MapPost("/messages/{id:int}/read", async (int id, CmsService c, CancellationToken ct) => { await c.MarkMessageReadAsync(id, ct); return Results.NoContent(); });
        g.MapDelete("/messages/{id:int}", async (int id, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteMessageAsync(ctx.Actor(), id, ct); return Results.NoContent(); });

        g.MapGet("/translations", (string? locale, CmsService c, CancellationToken ct) => c.TranslationsAsync(locale, ct));
        g.MapPut("/translations", async (TranslationRequest d, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.SaveTranslationAsync(ctx.Actor(), d.Locale, d.Key, d.Value, ct); return Results.NoContent(); });
        g.MapDelete("/translations/{locale}/{key}", async (string locale, string key, HttpContext ctx, CmsService c, CancellationToken ct) => { await c.DeleteTranslationAsync(ctx.Actor(), locale, key, ct); return Results.NoContent(); });

        g.MapGet("/audit", (string? action, string? entityType, int? page, CmsService c, CancellationToken ct) => c.AuditAsync(action, entityType, new PageQuery(page ?? 1, 50), ct));
    }

    private static string AuctionUrl(HttpContext ctx) => $"{ctx.PublicUrl()}/{(ctx.Request.IsArabic() ? "ar" : "en")}/auctions/";

    private static ProfileFiles KycFiles(IFormFileCollection f) => new(f.Upload("avatar"), f.Upload("crDocument"), f.Upload("vatCertificate"), f.Upload("idDocument"));

    private static PublisherFiles PublisherFiles(IFormFileCollection f) => new(f.Upload("logo"), f.Upload("background"), f.Upload("crDocument"), f.Upload("vatCertificate"), f.Upload("idDocument"));

    /// <summary>Proof files belong to the account they document, so the customer can see their own proof.</summary>
    private static async Task<Guid?> SaveProofAsync(IFormFileCollection f, HttpContext ctx, FileService files, ERecycleHub.Infrastructure.AppDbContext db, int ownerUserId, CancellationToken ct)
    {
        if (f.Upload("proof") is not { } upload) return null;
        var stored = await files.SaveAsync(upload, FileCategory.TransactionProof, ownerUserId, ct);
        await db.SaveChangesAsync(ct);
        return stored.Id;
    }
}

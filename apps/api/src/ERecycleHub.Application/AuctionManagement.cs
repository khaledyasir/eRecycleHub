using System.Data;
using System.Security.Cryptography;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

public sealed record SpecInput(string TitleEn, string? TitleAr, string? ValueEn, string? ValueAr);

public sealed record AuctionInput(
    int CountryId, int? CategoryId, string? TitleEn, string? TitleAr, string? ShortDescEn, string? ShortDescAr, string? DescEn, string? DescAr,
    decimal StartPrice, decimal BidIncrement, decimal InsuranceAmount, DateTime StartsAtUtc, DateTime EndsAtUtc, bool IsActive,
    string? VideoUrl, IReadOnlyList<SpecInput>? Specs, int? PublisherId,
    bool RemoveTerms = false, bool RemovePaymentConditions = false, bool RemoveContract = false);

public sealed record AuctionUploads(UploadInput? MainImage, UploadInput? Terms, UploadInput? PaymentConditions, UploadInput? Contract);

public sealed record ManagedAuctionRow(int Id, string RefNumber, string TitleEn, string TitleAr, Guid? MainImageFileId, decimal StartPrice, decimal CurrentPrice,
    decimal BidIncrement, DateTime StartsAtUtc, DateTime? EndsAtUtc, AuctionPhase Phase, bool IsActive, int BidCount, int? PublisherId, string? PublisherName, string CountryNameEn, string CountryNameAr);

public sealed record ManagedAuction(int Id, string RefNumber, int CountryId, int? CategoryId, int? PublisherId, string? PublisherName,
    string TitleEn, string TitleAr, string? ShortDescEn, string? ShortDescAr, string? DescEn, string? DescAr,
    decimal StartPrice, decimal CurrentPrice, decimal BidIncrement, decimal InsuranceAmount, DateTime StartsAtUtc, DateTime? EndsAtUtc,
    bool IsActive, string? VideoUrl, Guid? MainImageFileId, FileRef? Terms, FileRef? PaymentConditions, FileRef? Contract,
    IReadOnlyList<ImageDto> Images, IReadOnlyList<SpecDto> Specs, int BidCount, AuctionPhase Phase, DateTime CreatedAtUtc);

public sealed record ManagedBid(long Id, int UserId, string UserName, string? Phone, string Email, decimal Amount, DateTime CreatedAtUtc, bool IsLeading);

public sealed record ManagedAuctionFilter(string? View = "active", string? Status = null, string? Q = null, int? PublisherId = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null, int Page = 1, int PageSize = 20);

/// <summary>
/// Create/edit/activate/delete auctions for publishers (their own only) and administrators (any). Every query is scoped by
/// <see cref="Scope"/>, so an id belonging to another publisher behaves exactly like a missing one (404).
/// </summary>
public sealed class AuctionManagement(AppDbContext db, FileService files, TimeProvider clock, Audit audit)
{
    public const int MaxImages = 30;
    public const int MaxSpecs = 50;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private IQueryable<Auction> Scope(Actor actor) => actor.IsAdmin
        ? db.Auctions
        : actor.PublisherId is int pid ? db.Auctions.Where(a => a.PublisherId == pid) : db.Auctions.Where(_ => false);

    private static void RequireManager(Actor actor)
    {
        if (!actor.IsAdmin && actor.PublisherId is null) throw AppException.Forbidden();
    }

    public async Task<PagedResult<ManagedAuctionRow>> ListAsync(Actor actor, ManagedAuctionFilter f, CancellationToken ct = default)
    {
        RequireManager(actor);
        var now = Now;
        var q = Scope(actor).AsNoTracking();
        q = f.View == "archived" ? q.Where(a => !a.IsActive) : f.View == "all" ? q : q.Where(a => a.IsActive);
        q = AuctionQueries.InPhase(q, f.Status, now);
        if (Normalize.Text(f.Q) is { } term) q = q.Where(a => a.TitleEn.Contains(term) || a.TitleAr.Contains(term) || a.RefNumber == term);
        if (actor.IsAdmin && f.PublisherId is not null) q = f.PublisherId == 0 ? q.Where(a => a.PublisherId == null) : q.Where(a => a.PublisherId == f.PublisherId);
        if (f.FromUtc is not null) q = q.Where(a => a.StartsAtUtc >= f.FromUtc);
        if (f.ToUtc is not null) q = q.Where(a => a.EndsAtUtc <= f.ToUtc);
        var page = new PageQuery(f.Page, f.PageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Id).Skip(page.Skip).Take(page.SafeSize).Select(a => new ManagedAuctionRow(
            a.Id, a.RefNumber, a.TitleEn, a.TitleAr, a.MainImageFileId, a.StartPrice, a.CurrentPrice, a.BidIncrement, a.StartsAtUtc, a.EndsAtUtc,
            AuctionPhase.Upcoming, a.IsActive, a.BidCount, a.PublisherId,
            a.Publisher == null ? null : a.Publisher.FirstName + (a.Publisher.LastName == "" ? "" : " " + a.Publisher.LastName),
            a.Country!.NameEn, a.Country.NameAr)).ToListAsync(ct);
        return new(rows.Select(r => r with { Phase = AuctionRules.Phase(r.StartsAtUtc, r.EndsAtUtc, now) }).ToList(), page.SafePage, page.SafeSize, total);
    }

    public async Task<ManagedAuction> GetAsync(Actor actor, int id, CancellationToken ct = default)
    {
        RequireManager(actor);
        var a = await Scope(actor).AsNoTracking()
            .Include(x => x.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .Include(x => x.Specs.OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
            .Include(x => x.Publisher)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("auction");
        var ids = new[] { a.TermsFileId, a.PaymentConditionsFileId, a.ContractFileId }.Where(x => x != null).Select(x => x!.Value).ToList();
        var docs = await db.Files.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => new FileRef(f.Id, f.OriginalName, f.ContentType), ct);
        FileRef? Doc(Guid? fid) => fid is not null && docs.TryGetValue(fid.Value, out var d) ? d : null;
        return new ManagedAuction(a.Id, a.RefNumber, a.CountryId, a.CategoryId, a.PublisherId, a.Publisher?.DisplayName, a.TitleEn, a.TitleAr, a.ShortDescEn, a.ShortDescAr,
            a.DescEn, a.DescAr, a.StartPrice, a.CurrentPrice, a.BidIncrement, a.InsuranceAmount, a.StartsAtUtc, a.EndsAtUtc, a.IsActive, a.VideoUrl,
            a.MainImageFileId, Doc(a.TermsFileId), Doc(a.PaymentConditionsFileId), Doc(a.ContractFileId),
            a.Images.Select(i => new ImageDto(i.Id, i.FileId, i.Title, i.SortOrder)).ToList(),
            a.Specs.Select(s => new SpecDto(s.Id, s.TitleEn, s.TitleAr, s.ValueEn, s.ValueAr, s.SortOrder)).ToList(),
            a.BidCount, AuctionRules.Phase(a.StartsAtUtc, a.EndsAtUtc, Now), a.CreatedAtUtc);
    }

    public async Task<int> CreateAsync(Actor actor, AuctionInput input, AuctionUploads uploads, CancellationToken ct = default)
    {
        RequireManager(actor);
        if (uploads.MainImage is null) throw new AppException("auction.main_image_required");
        var publisherId = await ResolvePublisherAsync(actor, input.PublisherId, ct);
        await ValidateAsync(input, ct);
        if (input.EndsAtUtc <= Now) throw new AppException("auction.end_in_past");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = Now;
        var a = new Auction
        {
            RefNumber = await NewRefNumberAsync(ct),
            CountryId = input.CountryId,
            CategoryId = input.CategoryId,
            PublisherId = publisherId,
            TitleEn = "",
            TitleAr = "",
            StartPrice = input.StartPrice,
            CurrentPrice = input.StartPrice,
            BidIncrement = input.BidIncrement,
            InsuranceAmount = input.InsuranceAmount,
            StartsAtUtc = input.StartsAtUtc,
            EndsAtUtc = input.EndsAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        ApplyText(a, input);
        a.MainImageFileId = (await files.SaveAsync(uploads.MainImage, FileCategory.AuctionImage, actor.UserId, ct)).Id;
        await ApplyDocumentsAsync(a, input, uploads, actor, ct);
        a.Specs = ToSpecs(input.Specs);
        db.Auctions.Add(a);
        audit.Record(actor, "auction.create", nameof(Auction), null, new { a.TitleEn, publisherId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return a.Id;
    }

    /// <summary>
    /// Edits run with the auction row locked (same lock the bidding service takes), so a first bid cannot slip in between the
    /// "has bids?" check and the price update. Once bids exist, start price, increment, insurance, start time and country are frozen
    /// and the end time can only move to a future moment.
    /// </summary>
    public async Task UpdateAsync(Actor actor, int id, AuctionInput input, AuctionUploads uploads, CancellationToken ct = default)
    {
        RequireManager(actor);
        await ValidateAsync(input, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAsync(id, ct);
        var a = await Scope(actor).Include(x => x.Specs).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("auction");
        var hasBids = await db.Bids.AnyAsync(b => b.AuctionId == id, ct);
        var now = Now;

        if (hasBids)
        {
            if (input.StartPrice != a.StartPrice || input.BidIncrement != a.BidIncrement || input.InsuranceAmount != a.InsuranceAmount
                || input.StartsAtUtc != a.StartsAtUtc || input.CountryId != a.CountryId)
                throw AppException.Conflict("auction.terms_frozen");
            if (input.EndsAtUtc != a.EndsAtUtc && input.EndsAtUtc <= now) throw new AppException("auction.end_in_past");
        }
        else
        {
            a.StartPrice = input.StartPrice;
            a.CurrentPrice = input.StartPrice;
            a.BidIncrement = input.BidIncrement;
            a.InsuranceAmount = input.InsuranceAmount;
            a.StartsAtUtc = input.StartsAtUtc;
            a.CountryId = input.CountryId;
        }
        a.EndsAtUtc = input.EndsAtUtc;
        a.CategoryId = input.CategoryId;
        if (actor.IsAdmin && input.PublisherId != a.PublisherId)
        {
            if (hasBids) throw AppException.Conflict("auction.terms_frozen");
            a.PublisherId = await ResolvePublisherAsync(actor, input.PublisherId, ct);
        }
        ApplyText(a, input);
        if (uploads.MainImage is not null) a.MainImageFileId = (await files.SaveAsync(uploads.MainImage, FileCategory.AuctionImage, actor.UserId, ct)).Id;
        await ApplyDocumentsAsync(a, input, uploads, actor, ct);
        db.AuctionSpecs.RemoveRange(a.Specs);
        a.Specs = ToSpecs(input.Specs);
        a.UpdatedAtUtc = now;
        audit.Record(actor, "auction.update", nameof(Auction), id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> SetActiveAsync(Actor actor, int id, bool active, CancellationToken ct = default)
    {
        RequireManager(actor);
        var rows = await Scope(actor).Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, active).SetProperty(a => a.UpdatedAtUtc, Now), ct);
        if (rows == 0) throw AppException.NotFound("auction");
        audit.Record(actor, active ? "auction.activate" : "auction.deactivate", nameof(Auction), id);
        await db.SaveChangesAsync(ct);
        return active;
    }

    /// <summary>Auctions with bids cannot be deleted (deactivate them instead), as in the legacy system.</summary>
    public async Task DeleteAsync(Actor actor, int id, CancellationToken ct = default)
    {
        RequireManager(actor);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAsync(id, ct);
        var a = await Scope(actor).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("auction");
        if (await db.Bids.AnyAsync(b => b.AuctionId == id, ct)) throw AppException.Conflict("auction.has_bids");
        if (await db.WalletTransactions.AnyAsync(t => t.AuctionId == id, ct)) throw AppException.Conflict("auction.has_transactions");
        await db.Wishlist.Where(w => w.AuctionId == id).ExecuteDeleteAsync(ct);
        await db.AuctionWatches.Where(w => w.AuctionId == id).ExecuteDeleteAsync(ct);
        await db.Reviews.Where(r => r.AuctionId == id).ExecuteDeleteAsync(ct);
        db.Auctions.Remove(a);
        audit.Record(actor, "auction.delete", nameof(Auction), id, new { a.TitleEn, a.RefNumber });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<ImageDto>> AddImagesAsync(Actor actor, int id, IReadOnlyList<UploadInput> uploads, CancellationToken ct = default)
    {
        RequireManager(actor);
        if (!await Scope(actor).AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound("auction");
        var existing = await db.AuctionImages.CountAsync(i => i.AuctionId == id, ct);
        if (uploads.Count == 0) throw new AppException("file.empty");
        if (existing + uploads.Count > MaxImages) throw new AppException("auction.too_many_images", 400, new { max = MaxImages });
        var maxOrder = await db.AuctionImages.Where(i => i.AuctionId == id).MaxAsync(i => (int?)i.SortOrder, ct) ?? 0;
        var added = new List<AuctionImage>();
        foreach (var u in uploads)
        {
            var f = await files.SaveAsync(u, FileCategory.AuctionImage, actor.UserId, ct);
            var img = new AuctionImage { AuctionId = id, FileId = f.Id, Title = Path.GetFileNameWithoutExtension(f.OriginalName), SortOrder = ++maxOrder };
            db.AuctionImages.Add(img);
            added.Add(img);
        }
        audit.Record(actor, "auction.images_add", nameof(Auction), id, new { count = uploads.Count });
        await db.SaveChangesAsync(ct);
        return added.Select(i => new ImageDto(i.Id, i.FileId, i.Title, i.SortOrder)).ToList();
    }

    public async Task DeleteImageAsync(Actor actor, int id, int imageId, CancellationToken ct = default)
    {
        RequireManager(actor);
        var rows = await db.AuctionImages.Where(i => i.Id == imageId && i.AuctionId == id && Scope(actor).Any(a => a.Id == id)).ExecuteDeleteAsync(ct);
        if (rows == 0) throw AppException.NotFound("image");
        audit.Record(actor, "auction.image_delete", nameof(Auction), id, new { imageId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ManagedBid>> BidsAsync(Actor actor, int id, PageQuery page, CancellationToken ct = default)
    {
        RequireManager(actor);
        if (!await Scope(actor).AnyAsync(a => a.Id == id, ct)) throw AppException.NotFound("auction");
        var q = db.Bids.AsNoTracking().Where(b => b.AuctionId == id);
        var leaderId = await q.OrderByDescending(b => b.Amount).ThenBy(b => b.Id).Select(b => (long?)b.Id).FirstOrDefaultAsync(ct);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(b => b.Id).Skip(page.Skip).Take(page.SafeSize)
            .Select(b => new ManagedBid(b.Id, b.UserId, b.User!.FullName, b.User.Phone, b.User.Email, b.Amount, b.CreatedAtUtc, b.Id == leaderId)).ToListAsync(ct);
        return new(items, page.SafePage, page.SafeSize, total);
    }

    private Task LockAsync(int id, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"SELECT Id AS Value FROM Auctions WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}").ToListAsync(ct);

    private async Task<int?> ResolvePublisherAsync(Actor actor, int? requested, CancellationToken ct)
    {
        if (!actor.IsAdmin) return actor.PublisherId;
        if (requested is null or 0) return null;
        if (!await db.Publishers.AnyAsync(p => p.Id == requested, ct)) throw new AppException("publisher.invalid");
        return requested;
    }

    private async Task ValidateAsync(AuctionInput i, CancellationToken ct)
    {
        if (Normalize.Text(i.TitleEn) is null && Normalize.Text(i.TitleAr) is null) throw new AppException("auction.title_required");
        if ((i.TitleEn?.Length ?? 0) > 300 || (i.TitleAr?.Length ?? 0) > 300) throw new AppException("auction.title_too_long");
        if (!Money.IsValidPositive(i.StartPrice)) throw new AppException("auction.invalid_start_price");
        if (!Money.IsValidPositive(i.BidIncrement)) throw new AppException("auction.invalid_increment");
        if (!Money.IsValidNonNegative(i.InsuranceAmount)) throw new AppException("auction.invalid_insurance");
        if (i.EndsAtUtc <= i.StartsAtUtc) throw new AppException("auction.end_before_start");
        if (i.VideoUrl is { Length: > 0 } v && !(Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))
            throw new AppException("auction.invalid_video_url");
        if (i.Specs is { Count: > MaxSpecs }) throw new AppException("auction.too_many_specs", 400, new { max = MaxSpecs });
        if (i.Specs?.Any(s => Normalize.Text(s.TitleEn) is null && Normalize.Text(s.TitleAr) is null) == true) throw new AppException("auction.spec_title_required");
        if (!await db.Countries.AnyAsync(c => c.Id == i.CountryId, ct)) throw new AppException("country.invalid");
        if (i.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == i.CategoryId && c.CountryId == i.CountryId, ct))
            throw new AppException("category.invalid");
    }

    private static void ApplyText(Auction a, AuctionInput i)
    {
        var en = Normalize.Text(i.TitleEn);
        var ar = Normalize.Text(i.TitleAr);
        a.TitleEn = en ?? ar!;
        a.TitleAr = ar ?? en!;
        a.ShortDescEn = Normalize.Text(i.ShortDescEn);
        a.ShortDescAr = Normalize.Text(i.ShortDescAr);
        a.DescEn = RichText.Sanitize(i.DescEn);
        a.DescAr = RichText.Sanitize(i.DescAr);
        a.VideoUrl = Normalize.Text(i.VideoUrl);
        a.IsActive = i.IsActive;
    }

    private async Task ApplyDocumentsAsync(Auction a, AuctionInput i, AuctionUploads u, Actor actor, CancellationToken ct)
    {
        async Task<Guid?> Doc(UploadInput? file, bool remove, Guid? current) =>
            file is not null ? (await files.SaveAsync(file, FileCategory.AuctionDocument, actor.UserId, ct)).Id : remove ? null : current;
        a.TermsFileId = await Doc(u.Terms, i.RemoveTerms, a.TermsFileId);
        a.PaymentConditionsFileId = await Doc(u.PaymentConditions, i.RemovePaymentConditions, a.PaymentConditionsFileId);
        a.ContractFileId = await Doc(u.Contract, i.RemoveContract, a.ContractFileId);
    }

    private static List<AuctionSpec> ToSpecs(IReadOnlyList<SpecInput>? specs) => (specs ?? []).Select((s, idx) =>
    {
        var en = Normalize.Text(s.TitleEn);
        var ar = Normalize.Text(s.TitleAr);
        return new AuctionSpec { TitleEn = en ?? ar!, TitleAr = ar ?? en!, ValueEn = Normalize.Text(s.ValueEn), ValueAr = Normalize.Text(s.ValueAr), SortOrder = idx };
    }).ToList();

    private async Task<string> NewRefNumberAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
            if (!await db.Auctions.AnyAsync(a => a.RefNumber == candidate, ct)) return candidate;
        }
        throw new InvalidOperationException("Could not allocate a reference number.");
    }
}

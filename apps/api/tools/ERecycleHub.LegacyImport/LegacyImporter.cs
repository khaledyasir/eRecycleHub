using System.Globalization;
using System.Text;
using ERecycleHub.Application;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.LegacyImport;

public sealed record ImportOptions(string SourceConnection, string TargetConnection, string? LegacyFilesRoot, string StorageRoot, string ReportDirectory, bool DryRun);

/// <summary>
/// One-way copy of the legacy eRecycleHub database into a freshly migrated, EMPTY rebuild database.
/// The legacy database is only read. Identifiers are preserved. Every lossy or corrective conversion is written to the report.
/// See docs/DATABASE-MAPPING.md for the rules.
/// </summary>
public sealed class LegacyImporter(ImportOptions o)
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private readonly PasswordService _passwords = new();
    private readonly Dictionary<string, Guid?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _notes = [];
    private readonly StringBuilder _money = new("Table,Id,Column,LegacyValue,ImportedValue\n");
    private readonly Dictionary<string, (int legacy, int imported)> _counts = [];
    private readonly DateTime _now = DateTime.UtcNow;
    private AppDbContext _db = null!;
    private int _dateAnomalies;

    public sealed record Result(bool Success, string ReportPath, IReadOnlyDictionary<string, (int legacy, int imported)> Counts, IReadOnlyList<string> Notes);

    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(o.TargetConnection).Options;
        await using (_db = new AppDbContext(options))
        {
            await _db.Database.MigrateAsync(ct);
            if (await _db.Users.AnyAsync(ct) || await _db.Auctions.AnyAsync(ct))
                throw new InvalidOperationException("The target database is not empty. The importer only runs into an empty, freshly migrated database.");

            await using var src = new SqlConnection(o.SourceConnection);
            await src.OpenAsync(ct);

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await ImportAllAsync(src, ct);
                if (o.DryRun)
                {
                    await tx.RollbackAsync(ct);
                    _notes.Add("DRY RUN: everything was rolled back; no data was written (copied files, if any, are left in the storage folder).");
                }
                else await tx.CommitAsync(ct);
            });
        }
        var report = await WriteReportAsync(ct);
        return new Result(true, report, _counts, _notes);
    }

    // ------------------------------------------------------------------ tables

    private async Task ImportAllAsync(SqlConnection src, CancellationToken ct)
    {
        var countries = await ReadAsync(src, "SELECT * FROM Country", ct);
        await InsertAsync("Countries", countries.Select(r => new Country
        {
            Id = I(r, "Id"), NameEn = S(r, "Name") ?? $"Country {I(r, "Id")}", NameAr = S(r, "NameAr") ?? S(r, "Name") ?? $"دولة {I(r, "Id")}",
            Code = S(r, "Code"), IsActive = B(r, "IsActive"),
        }).ToList(), countries.Count, ct);
        // File references need user ids, so countries' flags are attached after users exist.

        var users = await ReadAsync(src, "SELECT * FROM [User] ORDER BY Id", ct);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userRows = new List<User>();
        foreach (var r in users)
        {
            var id = I(r, "Id");
            var email = (S(r, "Email") ?? "").Trim().ToLowerInvariant();
            var active = B(r, "IsActive");
            if (email.Length == 0 || !email.Contains('@'))
            {
                email = $"missing+{id}@invalid.local";
                active = false;
                _notes.Add($"User {id}: no valid e-mail; imported as {email} and deactivated.");
            }
            else if (!seenEmails.Add(email))
            {
                var original = email;
                email = $"dup+{id}+{original}".Replace("@", "_at_") + "@invalid.local";
                active = false;
                _notes.Add($"User {id}: duplicate e-mail {original}; imported as {email} and deactivated (lowest id keeps the address).");
            }
            // Legacy publishers (RoleId 2) become customers; their Publisher row (imported below) is what makes them sellers.
            var role = I(r, "RoleId") switch { 1 => UserRole.Admin, _ => UserRole.Customer };
            userRows.Add(new User
            {
                Id = id, CountryId = I(r, "CountryId"), FullName = S(r, "FullName") ?? email, Email = email, Phone = S(r, "Phone"),
                PasswordHash = Password(id, S(r, "Password")), Role = role, IsActive = active, IsDeleted = B(r, "IsDeleted"),
                WalletBalance = Money("User", id, "Wallet", D(r, "Wallet") ?? 0), CompanyName = S(r, "CompanyName"), CrNumber = S(r, "CrNumber"),
                VatNumber = S(r, "VatNumber"), BankName = S(r, "BankName"), CardNumber = S(r, "CardNumber"), AccountNumber = S(r, "AccountNumber"),
                CreatedAtUtc = _now,
            });
        }
        await InsertAsync("Users", userRows, users.Count, ct);

        // Files owned by users (now that the ids exist), then the remaining file columns.
        foreach (var (u, r) in userRows.Zip(users))
        {
            u.AvatarFileId = await FileAsync(S(r, "Image"), FileCategory.UserAvatar, u.Id, ct);
            u.CrDocumentFileId = await FileAsync(S(r, "CR"), FileCategory.KycDocument, u.Id, ct);
            u.VatCertificateFileId = await FileAsync(S(r, "VatCertifcate"), FileCategory.KycDocument, u.Id, ct);
            u.IdDocumentFileId = await FileAsync(S(r, "IdNumber"), FileCategory.KycDocument, u.Id, ct);
            // Legacy /Clients showed every active customer's picture; keep exactly those as "featured clients".
            u.IsFeaturedClient = u.Role == UserRole.Customer && u.IsActive && !u.IsDeleted && u.AvatarFileId is not null;
        }
        var countryById = await _db.Countries.ToDictionaryAsync(c => c.Id, ct);
        foreach (var r in countries) countryById[I(r, "Id")].FlagFileId = await FileAsync(S(r, "Image"), FileCategory.Cms, null, ct);
        await _db.SaveChangesAsync(ct);

        // Opening balance in the ledger = the legacy balance (the legacy transaction history does not reliably explain it).
        foreach (var u in userRows.Where(u => u.WalletBalance != 0))
            _db.WalletLedger.Add(new WalletLedgerEntry { UserId = u.Id, Amount = u.WalletBalance, BalanceAfter = u.WalletBalance, Reason = LedgerReason.MigrationOpeningBalance, CreatedAtUtc = _now });
        await _db.SaveChangesAsync(ct);

        var publishers = await ReadAsync(src, "SELECT * FROM Publisher", ct);
        var pubRows = new List<Publisher>();
        foreach (var r in publishers)
        {
            var userId = I(r, "UserId");
            if (pubRows.Any(p => p.UserId == userId))
            {
                _notes.Add($"Publisher {I(r, "Id")}: second publisher row for user {userId}; skipped.");
                continue;
            }
            pubRows.Add(new Publisher
            {
                Id = I(r, "Id"), UserId = userId, CountryId = I(r, "CountryId"), FirstName = S(r, "FirstName") ?? $"Publisher {I(r, "Id")}", LastName = S(r, "LastName") ?? "",
                Address = S(r, "Address"), SecondMobile = S(r, "SecoundMobile"), CreatedAtUtc = _now,
                LogoFileId = await FileAsync(S(r, "Image"), FileCategory.PublisherImage, userId, ct),
                BackgroundFileId = await FileAsync(S(r, "Background"), FileCategory.PublisherImage, userId, ct),
            });
        }
        await InsertAsync("Publishers", pubRows, publishers.Count, ct);

        var categories = await ReadAsync(src, "SELECT * FROM Category", ct);
        var catRows = new List<Category>();
        foreach (var r in categories)
            catRows.Add(new Category
            {
                Id = I(r, "Id"), CountryId = I(r, "CountryId"), TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? "", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? "",
                SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
                IconFileId = await FileAsync(S(r, "Icon"), FileCategory.Cms, null, ct), ImageFileId = await FileAsync(S(r, "Image"), FileCategory.Cms, null, ct),
            });
        await InsertAsync("Categories", catRows, categories.Count, ct);

        await ImportAuctionsAsync(src, pubRows.Select(p => p.Id).ToHashSet(), ct);
        await ImportMoneyHistoryAsync(src, ct);
        await ImportEngagementAsync(src, ct);
        await ImportContentAsync(src, ct);
    }

    private async Task ImportAuctionsAsync(SqlConnection src, HashSet<int> publisherIds, CancellationToken ct)
    {
        var auctions = await ReadAsync(src, "SELECT * FROM Auction ORDER BY Id", ct);
        var bids = await ReadAsync(src, "SELECT * FROM AuctionBid ORDER BY Id", ct);
        var bidsByAuction = bids.GroupBy(b => I(b, "AuctionId")).ToDictionary(g => g.Key, g => g.ToList());
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Auction>();
        foreach (var r in auctions)
        {
            var id = I(r, "Id");
            var pub = N(r, "PublisherId");
            if (pub is <= 0) pub = null;
            if (pub is not null && !publisherIds.Contains(pub.Value))
            {
                _notes.Add($"Auction {id}: publisher {pub} does not exist; imported as platform-owned.");
                pub = null;
            }
            var refNo = S(r, "RefNumber");
            if (refNo is null || refNo.Length > 32 || !refs.Add(refNo))
            {
                var replacement = $"L{id}";
                if (refNo is not null) _notes.Add($"Auction {id}: reference '{refNo}' missing/duplicate/too long; set to {replacement}.");
                refNo = replacement;
                refs.Add(refNo);
            }
            var start = Utc(DT(r, "StartDate"))!.Value;
            var end = Utc(DT(r, "EndDate"));
            if (end is not null && end <= start)
            {
                _notes.Add($"Auction {id}: end {end:u} is not after start {start:u}; end cleared (treated as ended).");
                end = null;
            }
            var startPrice = Money("Auction", id, "StartPrice", D(r, "StartPrice") ?? 0);
            var increment = Money("Auction", id, "IncreaseRange", D(r, "IncreaseRange") ?? 0);
            var myBids = bidsByAuction.GetValueOrDefault(id) ?? [];
            var maxBid = myBids.Count == 0 ? (decimal?)null : myBids.Max(b => Money(null, 0, null, D(b, "Amount") ?? 0));
            var legacyCurrent = Money("Auction", id, "CurrentPrice", D(r, "CurrentPrice") ?? 0);
            var current = Math.Max(startPrice, maxBid ?? startPrice);
            if (current != legacyCurrent) _notes.Add($"Auction {id}: CurrentPrice {legacyCurrent} differs from max(start price, highest bid) = {current}; corrected.");
            if (startPrice < 0 || increment < 0) throw new InvalidOperationException($"Auction {id} has a negative price; fix the source data first.");

            rows.Add(new Auction
            {
                Id = id, RefNumber = refNo, CountryId = I(r, "CountryId"), PublisherId = pub, CategoryId = null,
                TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? $"Auction {id}", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? $"مزاد {id}",
                ShortDescEn = S(r, "ShortDescEn"), ShortDescAr = S(r, "ShortDescAr"), DescEn = RichText.Sanitize(S(r, "DescEn")), DescAr = RichText.Sanitize(S(r, "DescAr")),
                StartPrice = startPrice, CurrentPrice = current, BidIncrement = increment,
                InsuranceAmount = Math.Max(0, Money("Auction", id, "InsuranceAmount", D(r, "InsuranceAmount") ?? 0)),
                StartsAtUtc = start, EndsAtUtc = end, IsActive = B(r, "IsActive"), VideoUrl = Url(S(r, "Video")),
                CreatedAtUtc = Utc(DT(r, "PreviewTime")) ?? start, UpdatedAtUtc = _now, BidCount = myBids.Count,
                MainImageFileId = await FileAsync(S(r, "MainImage"), FileCategory.AuctionImage, null, ct),
                TermsFileId = await FileAsync(S(r, "TermAndConditon"), FileCategory.AuctionDocument, null, ct),
                PaymentConditionsFileId = await FileAsync(S(r, "ConditionForPayment"), FileCategory.AuctionDocument, null, ct),
                ContractFileId = await FileAsync(S(r, "Contract"), FileCategory.AuctionDocument, null, ct),
            });
        }
        await InsertAsync("Auctions", rows, auctions.Count, ct);

        var specs = await ReadAsync(src, "SELECT * FROM AdditionalInformation ORDER BY Id", ct);
        var specRows = specs.Where(r => B(r, "IsActive")).Select(r => new AuctionSpec
        {
            Id = I(r, "Id"), AuctionId = I(r, "AuctionId"), TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? "", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? "",
            ValueEn = S(r, "ValueEn"), ValueAr = S(r, "ValueAr"), SortOrder = I(r, "Id"),
        }).ToList();
        await InsertAsync("AuctionSpecs", specRows, specs.Count, ct);

        var images = await ReadAsync(src, "SELECT * FROM AuctionImage ORDER BY Id", ct);
        var imageRows = new List<AuctionImage>();
        foreach (var r in images.Where(r => B(r, "IsActive")))
        {
            var name = S(r, "Image");
            var fileId = await FileAsync(name is null ? null : name.Contains('/') ? name : "App_File/AuctionImages/" + name, FileCategory.AuctionImage, null, ct);
            if (fileId is null) continue;
            imageRows.Add(new AuctionImage { Id = I(r, "Id"), AuctionId = I(r, "AuctionId"), FileId = fileId.Value, Title = S(r, "Title"), SortOrder = I(r, "Id") });
        }
        await InsertAsync("AuctionImages", imageRows, images.Count, ct);

        var bidRows = bids.Select(r => new Bid
        {
            Id = I(r, "Id"), AuctionId = I(r, "AuctionId"), UserId = I(r, "UserId"), Amount = Money("AuctionBid", I(r, "Id"), "Amount", D(r, "Amount") ?? 0),
            CreatedAtUtc = Utc(DT(r, "CreatedDate"))!.Value,
        }).Where(b => b.Amount > 0).ToList();
        if (bidRows.Count != bids.Count) _notes.Add($"{bids.Count - bidRows.Count} bids with a non-positive amount were skipped.");
        await InsertAsync("Bids", bidRows, bids.Count, ct);
    }

    private async Task ImportMoneyHistoryAsync(SqlConnection src, CancellationToken ct)
    {
        var txs = await ReadAsync(src, "SELECT * FROM [Transaction] ORDER BY Id", ct);
        var rows = new List<WalletTransaction>();
        foreach (var r in txs)
        {
            var id = I(r, "Id");
            var amount = Money("Transaction", id, "Amount", D(r, "Amount") ?? 0);
            if (!string.Equals(S(r, "Type"), "Credit", StringComparison.OrdinalIgnoreCase) && S(r, "Type") is not null)
            {
                _notes.Add($"Transaction {id}: type '{S(r, "Type")}' is not a credit; skipped.");
                continue;
            }
            if (amount <= 0)
            {
                _notes.Add($"Transaction {id}: amount {amount} is not positive; skipped.");
                continue;
            }
            var status = (S(r, "Status") ?? "").ToLowerInvariant() switch
            {
                "approved" => WalletTransactionStatus.Approved,
                "rejected" => WalletTransactionStatus.Rejected,
                "pending" => WalletTransactionStatus.Pending,
                var other => Note(WalletTransactionStatus.Pending, $"Transaction {id}: unknown status '{other}'; imported as Pending."),
            };
            var userId = I(r, "UserId");
            rows.Add(new WalletTransaction
            {
                Id = id, UserId = userId, Amount = amount, Status = status, CreatedAtUtc = Utc(DT(r, "CreatedDate"))!.Value, AuctionId = N(r, "AuctionId"),
                ProofFileId = await FileAsync(S(r, "Files"), FileCategory.TransactionProof, userId, ct), Note = "Imported from the legacy system",
            });
        }
        await InsertAsync("WalletTransactions", rows, txs.Count, ct);

        // Reconciliation only: legacy balances are kept as they are, differences are listed for review.
        var approved = rows.Where(t => t.Status == WalletTransactionStatus.Approved).GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        foreach (var u in await _db.Users.AsNoTracking().Select(u => new { u.Id, u.WalletBalance }).ToListAsync(ct))
        {
            var credits = approved.GetValueOrDefault(u.Id);
            if (credits != u.WalletBalance) _notes.Add($"Wallet review: user {u.Id} balance {u.WalletBalance} vs approved credits {credits} (difference {u.WalletBalance - credits}).");
        }
    }

    private async Task ImportEngagementAsync(SqlConnection src, CancellationToken ct)
    {
        var wish = await ReadAsync(src, "SELECT * FROM Wishlist", ct);
        await InsertAsync("Wishlist", wish.Select(r => new WishlistItem { UserId = I(r, "UserId"), AuctionId = I(r, "AuctionId"), CreatedAtUtc = _now })
            .DistinctBy(w => (w.UserId, w.AuctionId)).ToList(), wish.Count, ct, identity: false);
        var watch = await ReadAsync(src, "SELECT * FROM NotifyMe", ct);
        await InsertAsync("AuctionWatches", watch.Select(r => new AuctionWatch { UserId = I(r, "UserId"), AuctionId = I(r, "AuctionId"), CreatedAtUtc = _now })
            .DistinctBy(w => (w.UserId, w.AuctionId)).ToList(), watch.Count, ct, identity: false);

        var notes = await ReadAsync(src, "SELECT * FROM Notification", ct);
        await InsertAsync("Notifications", notes.Select(r => new Notification
        {
            Id = I(r, "Id"), UserId = I(r, "ToUserID"), FromUserId = N(r, "FromUserID") is > 0 ? N(r, "FromUserID") : null, Kind = NotificationKinds.Legacy,
            Title = Trim(S(r, "NotificationTitle"), 400), Body = Trim(S(r, "NotificationDesciption"), 4000), Url = Trim(S(r, "NotificationURL"), 1000), IsRead = B(r, "IsRead"),
            CreatedAtUtc = Utc(DT(r, "CreatedDate"))!.Value,
        }).ToList(), notes.Count, ct);

        var reviews = await ReadAsync(src, "SELECT * FROM Review", ct);
        await InsertAsync("Reviews", reviews.Select(r =>
        {
            var rate = I(r, "Rate");
            if (rate is < 1 or > 5) _notes.Add($"Review {I(r, "Id")}: rating {rate} clamped to 1..5.");
            return new Review
            {
                Id = I(r, "Id"), AuctionId = I(r, "AuctionId"), UserId = I(r, "UserId"), Rating = Math.Clamp(rate, 1, 5), Comment = Trim(S(r, "Notes"), 4000),
                Name = S(r, "Name"), CreatedAtUtc = Utc(DT(r, "CreatedDate"))!.Value,
            };
        }).ToList(), reviews.Count, ct);

        // Legacy subscription requests were anonymous (an e-mail and a chosen password, no account). Applications now belong to a signed-in
        // user, so they are not imported; the count is reported so the owner can ask those applicants to apply again from their account.
        var legacyApps = (await ReadAsync(src, "SELECT * FROM SubscriptionRequest", ct)).Count;
        if (legacyApps > 0) _notes.Add($"{legacyApps} legacy subscription request(s) were not imported: applications now belong to a signed-in customer account.");

        var msgs = await ReadAsync(src, "SELECT * FROM ContactForm", ct);
        await InsertAsync("ContactMessages", msgs.Select(r => new ContactMessage
        {
            Id = I(r, "Id"), Name = S(r, "Name") ?? "", Email = S(r, "Email") ?? "", Mobile = S(r, "Mobile"), Message = Trim(S(r, "Message"), 4000) ?? "",
            CreatedAtUtc = Utc(DT(r, "CreatedDate"))!.Value, IsRead = true,
        }).ToList(), msgs.Count, ct);
    }

    private async Task ImportContentAsync(SqlConnection src, CancellationToken ct)
    {
        var info = await ReadAsync(src, "SELECT * FROM ContactInfo", ct);
        await InsertAsync("ContactInfos", info.Select(r => new ContactInfo
        {
            Id = I(r, "Id"), CountryId = I(r, "CountryId"), Mobile = S(r, "Mobile"), Address = S(r, "Address"), Email = S(r, "Email"), MapUrl = Url(S(r, "Map")),
            Facebook = Url(S(r, "Facebook")), Instagram = Url(S(r, "Instagram")), Twitter = Url(S(r, "Twitter")), Linkedin = Url(S(r, "Linkedin")),
        }).ToList(), info.Count, ct);

        var about = await ReadAsync(src, "SELECT * FROM AboutUs", ct);
        var aboutRows = new List<AboutSection>();
        foreach (var r in about)
            aboutRows.Add(new AboutSection
            {
                Id = I(r, "Id"), TitleEn = S(r, "TitleEn"), TitleAr = S(r, "TitleAr"), DescEn = RichText.Sanitize(S(r, "DescEn")), DescAr = RichText.Sanitize(S(r, "DescAr")),
                TotalBidders = N(r, "TotalBidder"), GuidelinesVideoUrl = Url(S(r, "GuidelinesVideo")),
                FirstImageFileId = await FileAsync(S(r, "FirstImage"), FileCategory.Cms, null, ct), SecondImageFileId = await FileAsync(S(r, "SecoundImage"), FileCategory.Cms, null, ct),
            });
        await InsertAsync("AboutSections", aboutRows, about.Count, ct);

        var benefits = await ReadAsync(src, "SELECT * FROM Benefit", ct);
        var benefitRows = new List<Benefit>();
        foreach (var r in benefits)
            benefitRows.Add(new Benefit
            {
                Id = I(r, "Id"), TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? "", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? "",
                DescEn = RichText.Sanitize(S(r, "DescEn")), DescAr = RichText.Sanitize(S(r, "DescAr")), SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
                IconFileId = await FileAsync(S(r, "Icon"), FileCategory.Cms, null, ct),
            });
        await InsertAsync("Benefits", benefitRows, benefits.Count, ct);

        var steps = await ReadAsync(src, "SELECT * FROM HowToBid", ct);
        await InsertAsync("HowToSteps", steps.Select(r => new HowToStep
        {
            Id = I(r, "Id"), TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? "", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? "",
            DescEn = RichText.Sanitize(S(r, "DescEn")), DescAr = RichText.Sanitize(S(r, "DescAr")), SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
        }).ToList(), steps.Count, ct);

        var faqCats = await ReadAsync(src, "SELECT * FROM FaqCategory", ct);
        await InsertAsync("FaqCategories", faqCats.Select(r => new FaqCategory
        {
            Id = I(r, "Id"), TitleEn = S(r, "TitleEn") ?? S(r, "TitleAr") ?? "", TitleAr = S(r, "TitleAr") ?? S(r, "TitleEn") ?? "", SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
        }).ToList(), faqCats.Count, ct);
        var faqs = await ReadAsync(src, "SELECT * FROM Faq", ct);
        await InsertAsync("Faqs", faqs.Select(r => new Faq
        {
            Id = I(r, "Id"), CategoryId = I(r, "CategoryId"), QuestionEn = S(r, "QuestionEn") ?? S(r, "QuestionAr") ?? "", QuestionAr = S(r, "QuestionAr") ?? S(r, "QuestionEn") ?? "",
            AnswerEn = RichText.Sanitize(S(r, "AnswerEn")), AnswerAr = RichText.Sanitize(S(r, "AnswerAr")), SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
        }).ToList(), faqs.Count, ct);

        var sliders = await ReadAsync(src, "SELECT * FROM Slider", ct);
        var sliderRows = new List<Slider>();
        foreach (var r in sliders)
            sliderRows.Add(new Slider
            {
                Id = I(r, "Id"), Title = S(r, "Title"), Link = Url(S(r, "Link")), Type = S(r, "Type") == "Mobile" ? "Mobile" : "Web", SortOrder = I(r, "SortOrder"),
                IsActive = B(r, "IsActive"), ImageFileId = await FileAsync(S(r, "Image"), FileCategory.Cms, null, ct),
            });
        await InsertAsync("Sliders", sliderRows, sliders.Count, ct);

        var anns = await ReadAsync(src, "SELECT * FROM Announcement", ct);
        var annRows = new List<Announcement>();
        foreach (var r in anns)
            annRows.Add(new Announcement
            {
                Id = I(r, "Id"), CountryId = I(r, "CountryId"), SortOrder = I(r, "SortOrder"), IsActive = B(r, "IsActive"),
                ImageFileId = await FileAsync(S(r, "Image"), FileCategory.Cms, null, ct),
            });
        await InsertAsync("Announcements", annRows, anns.Count, ct);

        var settings = await ReadAsync(src, "SELECT * FROM Setting", ct);
        await InsertAsync("Settings", settings.Select(r => new Setting
        {
            Id = I(r, "Id"),
            Key = I(r, "Id") switch { 3 => SettingKeys.PrivacyPolicy, 5 => SettingKeys.Terms, var n => $"legacy-{n}" },
            TitleEn = S(r, "TitleEn"), TitleAr = S(r, "TitleAr"), ValueEn = RichText.Sanitize(S(r, "ValueEn")), ValueAr = RichText.Sanitize(S(r, "ValueAr")),
        }).ToList(), settings.Count, ct);
        if (!settings.Any(r => I(r, "Id") == 3)) _notes.Add("Legacy Setting 3 (privacy policy) not found; /privacy will show an empty state until an admin writes it.");
        if (!settings.Any(r => I(r, "Id") == 5)) _notes.Add("Legacy Setting 5 (terms) not found; /terms will show an empty state until an admin writes it.");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Adds rows with their legacy ids (IDENTITY_INSERT) and records the count.</summary>
    private async Task InsertAsync<T>(string table, List<T> rows, int legacyCount, CancellationToken ct, bool identity = true) where T : class
    {
        _counts[table] = (legacyCount, rows.Count);
        if (rows.Count == 0) return;
        _db.Set<T>().AddRange(rows);
        // Table names come from the EF model (never from data) and are quoted as identifiers; identifiers cannot be SQL parameters.
#pragma warning disable EF1003
        var name = _db.Model.FindEntityType(typeof(T))!.GetTableName()!.Replace("]", "]]");
        if (identity) await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [" + name + "] ON", ct);
        await _db.SaveChangesAsync(ct);
        if (identity) await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [" + name + "] OFF", ct);
#pragma warning restore EF1003
    }

    private static async Task<List<Dictionary<string, object?>>> ReadAsync(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    private static int I(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
    private static int? N(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;
    private static bool B(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is bool b && b;
    private static double? D(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;
    private static DateTime? DT(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is DateTime d ? d : null;
    private static string? S(Dictionary<string, object?> r, string c) => r.TryGetValue(c, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
    private T Note<T>(T value, string note) { _notes.Add(note); return value; }

    private static string? Url(string? s) => s is not null && Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http") ? s : null;

    /// <summary>Legacy wall-clock values are US Central time (TimeHelper); DST-aware conversion to UTC.</summary>
    private DateTime? Utc(DateTime? central)
    {
        if (central is null) return null;
        var local = DateTime.SpecifyKind(central.Value, DateTimeKind.Unspecified);
        if (Central.IsInvalidTime(local)) { _dateAnomalies++; local = local.AddHours(1); }
        if (Central.IsAmbiguousTime(local)) _dateAnomalies++;
        return TimeZoneInfo.ConvertTimeToUtc(local, Central);
    }

    /// <summary>float → decimal(18,2), rounding half away from zero like the hardened legacy code; any change beyond float noise is listed.</summary>
    private decimal Money(string? table, int id, string? column, double value)
    {
        var dec = Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
        if (table is not null && Math.Abs((double)dec - value) > 0.0001)
            _money.Append(CultureInfo.InvariantCulture, $"{table},{id},{column},{value.ToString("R", CultureInfo.InvariantCulture)},{dec.ToString(CultureInfo.InvariantCulture)}\n");
        return dec;
    }

    /// <summary>Hashes plaintext with the modern hasher so users keep their password but no plaintext reaches the new database.</summary>
    private string? Password(int userId, string? stored)
    {
        if (stored is null) { _notes.Add($"User {userId}: no password; they must use \"forgot password\"."); return null; }
        return PasswordService.IsLegacyV1(stored) ? stored : _passwords.Hash(stored);
    }

    /// <summary>
    /// Maps a legacy file reference (full URL ".../App_File/Folder/name" or relative path) to a stored file: copied from the legacy
    /// App_File folder, type-checked by extension + magic bytes, and registered. Missing/rejected files are reported and become null.
    /// </summary>
    private async Task<Guid?> FileAsync(string? reference, FileCategory category, int? ownerUserId, CancellationToken ct)
    {
        if (reference is null) return null;
        if (_files.TryGetValue(reference, out var cached)) return cached;
        Guid? result = null;
        var idx = reference.IndexOf("App_File/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) _notes.Add($"File reference outside App_File ignored: {reference}");
        else if (o.LegacyFilesRoot is null) _notes.Add($"File not copied (no --files given): {reference}");
        else
        {
            var relative = Uri.UnescapeDataString(reference[(idx + "App_File/".Length)..]).Replace('/', Path.DirectorySeparatorChar);
            var source = Path.GetFullPath(Path.Combine(o.LegacyFilesRoot, relative));
            if (!source.StartsWith(Path.GetFullPath(o.LegacyFilesRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                _notes.Add($"File missing: {reference}");
            else
            {
                var ext = Path.GetExtension(source).ToLowerInvariant();
                var types = new Dictionary<string, string>
                {
                    [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
                    [".pdf"] = "application/pdf", [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                };
                if (!types.TryGetValue(ext, out var contentType) || !await LooksValidAsync(source, ext, ct))
                    _notes.Add($"File rejected (type/content check): {reference}");
                else
                {
                    var id = Guid.NewGuid();
                    var stored = Path.Combine("legacy", id.ToString("N") + ext).Replace('\\', '/');
                    var target = Path.Combine(Path.GetFullPath(o.StorageRoot), stored);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target);
                    var isPublic = category is not (FileCategory.KycDocument or FileCategory.TransactionProof);
                    _db.Files.Add(new StoredFile
                    {
                        Id = id, Category = category, IsPublic = isPublic, OwnerUserId = ownerUserId, OriginalName = Path.GetFileName(source), ContentType = contentType,
                        SizeBytes = new FileInfo(source).Length, StoragePath = stored, LegacyUrl = Trim(reference, 1000), CreatedAtUtc = _now,
                    });
                    result = id;
                }
            }
        }
        _files[reference] = result;
        return result;
    }

    private static async Task<bool> LooksValidAsync(string path, string ext, CancellationToken ct)
    {
        var head = new byte[12];
        await using var f = File.OpenRead(path);
        var n = await f.ReadAtLeastAsync(head, head.Length, false, ct);
        if (n < 4) return false;
        return ext switch
        {
            ".jpg" or ".jpeg" => head[0] == 0xFF && head[1] == 0xD8,
            ".png" => head[0] == 0x89 && head[1] == 0x50,
            ".gif" => head[0] == 0x47 && head[1] == 0x49,
            ".webp" => head[0] == 0x52 && head[1] == 0x49,
            ".pdf" => head[0] == 0x25 && head[1] == 0x50,
            ".docx" or ".xlsx" => head[0] == 0x50 && head[1] == 0x4B,
            ".doc" or ".xls" => head[0] == 0xD0 && head[1] == 0xCF,
            ".svg" => Encoding.UTF8.GetString(head, 0, n).Contains('<'), // served with a sandboxing CSP; never rendered inline as a document
            _ => false,
        };
    }

    private async Task<string> WriteReportAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(o.ReportDirectory);
        var sb = new StringBuilder($"# Legacy import report ({_now:u}){(o.DryRun ? " — DRY RUN" : "")}\n\n| Table | Legacy rows | Imported |\n|---|---|---|\n");
        foreach (var (t, (l, i)) in _counts) sb.Append(CultureInfo.InvariantCulture, $"| {t} | {l} | {i} |\n");
        sb.Append(CultureInfo.InvariantCulture, $"\nDate conversion anomalies (DST gaps/overlaps): {_dateAnomalies}\n\n## Notes ({_notes.Count})\n\n");
        foreach (var n in _notes) sb.Append("- ").Append(n).Append('\n');
        var path = Path.Combine(o.ReportDirectory, "import-report.md");
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        await File.WriteAllTextAsync(Path.Combine(o.ReportDirectory, "money-adjustments.csv"), _money.ToString(), ct);
        return path;
    }
}

using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.Application;

/// <summary>
/// DEVELOPMENT / TEST DATA ONLY. Creates well-known accounts (see README) and sample content. Refuses to run on a database that already has users.
/// Wallet balances are created through ledger entries so the ledger invariant holds from the start.
/// </summary>
public sealed class DevSeed(AppDbContext db, PasswordService passwords, FileService files, TimeProvider clock)
{
    public const string AdminEmail = "admin@test.local", AdminPassword = "AdminPass#2026";
    public const string CustomerEmail = "customer@test.local", Customer2Email = "customer2@test.local", CustomerPassword = "CustomerPass#2026";
    public const string PublisherEmail = "publisher@test.local", Publisher2Email = "publisher2@test.local", PublisherPassword = "PublisherPass#2026";

    private DateTime now;

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) throw new InvalidOperationException("The database already contains users; the dev seed only runs on an empty database.");
        now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var jordan = new Country { NameEn = "Jordan", NameAr = "الأردن", Code = "JO" };
        var uae = new Country { NameEn = "United Arab Emirates", NameAr = "الإمارات العربية المتحدة", Code = "AE" };
        db.Countries.AddRange(jordan, uae);
        await db.SaveChangesAsync(ct);

        var cats = new (string en, string ar, string color)[]
        {
            ("Scrap metal", "خردة المعادن", "#64748b"), ("Plastics", "البلاستيك", "#0ea5e9"), ("Paper & cardboard", "الورق والكرتون", "#a16207"),
            ("Electronics (e-waste)", "النفايات الإلكترونية", "#7c3aed"), ("Industrial equipment", "المعدات الصناعية", "#ea580c"), ("Vehicles & parts", "المركبات وقطعها", "#16a34a"),
        };
        var categories = new List<Category>();
        for (var i = 0; i < cats.Length; i++)
        {
            var c = new Category { CountryId = jordan.Id, TitleEn = cats[i].en, TitleAr = cats[i].ar, SortOrder = i + 1, IconFileId = await SvgAsync(Icon(cats[i].color), "category.svg", ct) };
            categories.Add(c);
        }
        db.Categories.AddRange(categories);

        var admin = User("Platform Admin", AdminEmail, AdminPassword, UserRole.Admin, jordan.Id, "0790000001");
        var c1 = User("Test Customer", CustomerEmail, CustomerPassword, UserRole.Customer, jordan.Id, "0790000002");
        var c2 = User("Second Customer", Customer2Email, CustomerPassword, UserRole.Customer, jordan.Id, "0790000003");
        var p1u = User("Green Cycle Co.", PublisherEmail, PublisherPassword, UserRole.Publisher, jordan.Id, "0790000004");
        var p2u = User("Amman Metals", Publisher2Email, PublisherPassword, UserRole.Publisher, jordan.Id, "0790000005");
        c1.CompanyName = "Test Trading LLC";
        c1.IsFeaturedClient = true;
        p1u.CompanyName = "Green Cycle Co.";
        p2u.CompanyName = "Amman Metals";
        db.Users.AddRange(admin, c1, c2, p1u, p2u);
        await db.SaveChangesAsync(ct);
        c1.AvatarFileId = await SvgAsync(Monogram("TT", "#0f5b46"), "client.svg", ct);

        var p1 = new Publisher { UserId = p1u.Id, CountryId = jordan.Id, FirstName = "Green Cycle", LastName = "Co.", Address = "Sahab Industrial City, Amman", SecondMobile = "0790000014", CreatedAtUtc = now,
            LogoFileId = await SvgAsync(Monogram("GC", "#15803d"), "logo.svg", ct), BackgroundFileId = await SvgAsync(Banner("#14532d", "#22c55e"), "banner.svg", ct) };
        var p2 = new Publisher { UserId = p2u.Id, CountryId = jordan.Id, FirstName = "Amman Metals", Address = "Zarqa Free Zone", CreatedAtUtc = now,
            LogoFileId = await SvgAsync(Monogram("AM", "#334155"), "logo.svg", ct), BackgroundFileId = await SvgAsync(Banner("#1e293b", "#64748b"), "banner.svg", ct) };
        db.Publishers.AddRange(p1, p2);
        await db.SaveChangesAsync(ct);

        // Opening balances through the ledger (500 each), like an approved admin credit.
        foreach (var u in new[] { c1, c2 })
        {
            var t = new WalletTransaction { UserId = u.Id, Amount = 500, Status = WalletTransactionStatus.Approved, CreatedAtUtc = now, CreatedByUserId = admin.Id, DecidedByUserId = admin.Id, DecidedAtUtc = now, Note = "Seed" };
            db.WalletTransactions.Add(t);
            await db.SaveChangesAsync(ct);
            u.WalletBalance = 500;
            db.WalletLedger.Add(new WalletLedgerEntry { UserId = u.Id, Amount = 500, BalanceAfter = 500, Reason = LedgerReason.AdminCredit, WalletTransactionId = t.Id, ActorUserId = admin.Id, CreatedAtUtc = now });
        }
        db.WalletTransactions.Add(new WalletTransaction { UserId = c1.Id, Amount = 75, Status = WalletTransactionStatus.Pending, CreatedAtUtc = now.AddHours(-3), CreatedByUserId = c1.Id });

        var specs = new[] { ("Quantity", "الكمية", "12 tonnes", "12 طن"), ("Location", "الموقع", "Sahab, Amman", "سحاب، عمّان"), ("Pickup", "الاستلام", "Buyer arranges transport", "يتولى المشتري النقل") };
        (string en, string ar, int cat, Publisher? pub, decimal start, decimal inc, decimal ins, double startDays, double endDays, string color)[] rows =
        [
            ("Open test auction", "مزاد اختبار مفتوح", 0, p1, 100, 10, 50, -1, 30, "#64748b"),
            ("Ended test auction", "مزاد اختبار منتهٍ", 0, p1, 100, 10, 0, -10, -1, "#475569"),
            ("Future test auction", "مزاد اختبار قادم", 1, p1, 100, 10, 0, 5, 30, "#0ea5e9"),
            ("High deposit auction", "مزاد بتأمين مرتفع", 4, p1, 100, 10, 99999, -1, 30, "#ea580c"),
            ("Baled PET bottles – 8 tonnes", "عبوات PET مكبوسة – 8 أطنان", 1, p1, 1200, 50, 100, -2, 3, "#0284c7"),
            ("Copper cable offcuts", "قصاصات كوابل نحاسية", 0, p2, 3500, 100, 250, -1, 2, "#b45309"),
            ("Corrugated cardboard (OCC) – monthly lot", "كرتون مضلع – دفعة شهرية", 2, p2, 400, 25, 0, -3, 6, "#a16207"),
            ("Decommissioned office laptops (40 units)", "أجهزة لابتوب مكتبية مستعملة (40 جهازاً)", 3, p1, 2000, 100, 200, 1, 9, "#7c3aed"),
            ("Hydraulic press – 150 t", "مكبس هيدروليكي – 150 طن", 4, p2, 8000, 250, 500, 2, 12, "#c2410c"),
            ("Aluminium cans – 3 tonnes", "علب ألمنيوم – 3 أطنان", 0, null, 900, 25, 0, -20, -5, "#94a3b8"),
            ("Forklift parts lot", "دفعة قطع رافعات شوكية", 5, p2, 650, 20, 0, -15, -2, "#16a34a"),
            ("Mixed HDPE crates", "صناديق HDPE مختلطة", 1, null, 300, 10, 0, -1, 14, "#38bdf8"),
        ];
        var auctions = new List<Auction>();
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var a = new Auction
            {
                RefNumber = (10000001 + i).ToString(), CountryId = jordan.Id, CategoryId = categories[r.cat].Id, PublisherId = r.pub?.Id,
                TitleEn = r.en, TitleAr = r.ar, ShortDescEn = "Sold as seen. Inspection by appointment.", ShortDescAr = "يباع بحالته. المعاينة بموعد مسبق.",
                DescEn = "<p>Lot description provided by the seller. Weights are approximate.</p><ul><li>Inspection by appointment</li><li>Payment within 3 working days</li></ul>",
                DescAr = "<p>وصف الدفعة مقدّم من البائع. الأوزان تقريبية.</p><ul><li>المعاينة بموعد مسبق</li><li>الدفع خلال 3 أيام عمل</li></ul>",
                StartPrice = r.start, CurrentPrice = r.start, BidIncrement = r.inc, InsuranceAmount = r.ins,
                StartsAtUtc = now.AddDays(r.startDays), EndsAtUtc = now.AddDays(r.endDays), CreatedAtUtc = now.AddDays(Math.Min(r.startDays, 0) - 1), UpdatedAtUtc = now,
                MainImageFileId = await SvgAsync(Lot(r.color, r.en), "lot.svg", ct),
                Specs = specs.Select((s, k) => new AuctionSpec { TitleEn = s.Item1, TitleAr = s.Item2, ValueEn = s.Item3, ValueAr = s.Item4, SortOrder = k }).ToList(),
            };
            a.Images.Add(new AuctionImage { FileId = await SvgAsync(Lot(Shade(r.color), r.en + " · 2"), "gallery.svg", ct), Title = "Photo 2", SortOrder = 1 });
            auctions.Add(a);
        }
        db.Auctions.AddRange(auctions);
        await db.SaveChangesAsync(ct);

        // History on two ended auctions and one live auction so dashboards, winners and reports have real data.
        await BidsAsync(auctions[9], [(c1, 925), (c2, 950), (c1, 1000)], -6, ct);
        await BidsAsync(auctions[10], [(c2, 670), (c1, 690)], -3, ct);
        await BidsAsync(auctions[5], [(c2, 3600)], -0.5, ct);

        db.Reviews.Add(new Review { AuctionId = auctions[9].Id, UserId = c1.Id, Name = c1.FullName, Rating = 5, Comment = "Smooth pickup, accurate weights.", CreatedAtUtc = now.AddDays(-4) });
        db.AuctionWatches.Add(new AuctionWatch { UserId = c1.Id, AuctionId = auctions[5].Id, CreatedAtUtc = now });
        db.Wishlist.Add(new WishlistItem { UserId = c1.Id, AuctionId = auctions[4].Id, CreatedAtUtc = now });

        db.ContactInfos.AddRange(
            new ContactInfo { CountryId = jordan.Id, Mobile = "+962 6 000 0000", Email = "info@erecyclehub.local", Address = "Amman, Jordan", MapUrl = "https://www.google.com/maps?q=Amman&output=embed" },
            new ContactInfo { CountryId = uae.Id, Mobile = "+971 4 000 0000", Email = "uae@erecyclehub.local", Address = "Dubai, UAE" });
        db.AboutSections.Add(new AboutSection
        {
            TitleEn = "A marketplace for recyclable materials", TitleAr = "سوق للمواد القابلة لإعادة التدوير",
            DescEn = "<p>eRecycleHub connects waste generators with licensed scrap dealers and recyclers through transparent online auctions.</p>",
            DescAr = "<p>يربط eRecycleHub منتجي النفايات بتجار الخردة ومعيدي التدوير المرخّصين عبر مزادات إلكترونية شفافة.</p>",
            TotalBidders = 12, FirstImageFileId = await SvgAsync(Banner("#0f5b46", "#34d399"), "about.svg", ct),
        });
        string[][] steps = [["Create an account", "أنشئ حساباً", "Register and complete your company details.", "سجّل وأكمل بيانات شركتك."],
            ["Top up your wallet", "اشحن محفظتك", "Request a top-up; an administrator approves it after payment.", "اطلب شحن المحفظة، ويوافق عليه المسؤول بعد الدفع."],
            ["Bid", "زايد", "Each bid must be at least the current price plus the increment.", "يجب أن تكون كل مزايدة مساوية للسعر الحالي مضافاً إليه مقدار الزيادة على الأقل."],
            ["Win and collect", "اربح واستلم", "The highest bid when the auction ends wins.", "أعلى مزايدة عند انتهاء المزاد هي الفائزة."]];
        db.HowToSteps.AddRange(steps.Select((s, i) => new HowToStep { TitleEn = s[0], TitleAr = s[1], DescEn = $"<p>{s[2]}</p>", DescAr = $"<p>{s[3]}</p>", SortOrder = i + 1 }));
        db.Benefits.AddRange(
            new Benefit { TitleEn = "Verified buyers", TitleAr = "مشترون موثّقون", DescEn = "<p>Bidders keep an insurance balance in their wallet.</p>", DescAr = "<p>يحتفظ المزايدون برصيد تأمين في محافظهم.</p>", SortOrder = 1, IconFileId = await SvgAsync(Icon("#0f5b46"), "b.svg", ct) },
            new Benefit { TitleEn = "Fair price discovery", TitleAr = "تسعير عادل", DescEn = "<p>Open bidding instead of private negotiation.</p>", DescAr = "<p>مزايدة مفتوحة بدلاً من التفاوض الخاص.</p>", SortOrder = 2, IconFileId = await SvgAsync(Icon("#0284c7"), "b.svg", ct) });
        var faqCat = new FaqCategory { TitleEn = "Bidding", TitleAr = "المزايدة", SortOrder = 1 };
        faqCat.Faqs.Add(new Faq { QuestionEn = "What is the insurance amount?", QuestionAr = "ما هو مبلغ التأمين؟", AnswerEn = "<p>The minimum wallet balance required to bid on an auction. It is not deducted.</p>", AnswerAr = "<p>الحد الأدنى لرصيد المحفظة المطلوب للمزايدة. لا يتم خصمه.</p>", SortOrder = 1 });
        faqCat.Faqs.Add(new Faq { QuestionEn = "How is the winner decided?", QuestionAr = "كيف يتم تحديد الفائز؟", AnswerEn = "<p>The highest bid when the auction ends.</p>", AnswerAr = "<p>أعلى مزايدة عند انتهاء المزاد.</p>", SortOrder = 2 });
        db.FaqCategories.Add(faqCat);
        db.Settings.AddRange(
            new Setting { Key = SettingKeys.Terms, TitleEn = "Terms and conditions", TitleAr = "الشروط والأحكام", ValueEn = "<p>Sample terms for development.</p>", ValueAr = "<p>شروط تجريبية لبيئة التطوير.</p>" },
            new Setting { Key = SettingKeys.PrivacyPolicy, TitleEn = "Privacy policy", TitleAr = "سياسة الخصوصية", ValueEn = "<p>Sample privacy policy for development.</p>", ValueAr = "<p>سياسة خصوصية تجريبية لبيئة التطوير.</p>" });
        db.Sliders.Add(new Slider { Title = "Recycle smarter", Link = "/auctions", Type = "Web", SortOrder = 1, ImageFileId = await SvgAsync(Banner("#0b3d2e", "#10b981"), "slide.svg", ct) });
        db.Announcements.Add(new Announcement { CountryId = jordan.Id, SortOrder = 1, ImageFileId = await SvgAsync(Banner("#78350f", "#f59e0b"), "announcement.svg", ct) });
        db.PublisherApplications.Add(new PublisherApplication { Name = "New Recycler", Email = "applicant@test.local", Mobile = "0790000099", Company = "New Recycler Ltd", CountryId = jordan.Id,
            PasswordHash = passwords.Hash("ApplicantPass#2026"), Status = ApplicationStatus.Pending, CreatedAtUtc = now.AddDays(-1),
            Categories = [new PublisherApplicationCategory { CategoryId = categories[1].Id }] });
        db.ContactMessages.Add(new ContactMessage { Name = "Visitor", Email = "visitor@test.local", Message = "Do you accept bulk glass?", CreatedAtUtc = now.AddHours(-5) });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private User User(string name, string email, string password, UserRole role, int country, string phone) => new()
    {
        FullName = name, Email = email, PasswordHash = passwords.Hash(password), Role = role, CountryId = country, Phone = phone, CreatedAtUtc = now,
    };

    private async Task BidsAsync(Auction a, (User user, decimal amount)[] bids, double daysAgo, CancellationToken ct)
    {
        var at = now.AddDays(daysAgo);
        foreach (var (user, amount) in bids)
        {
            at = at.AddMinutes(37);
            db.Bids.Add(new Bid { AuctionId = a.Id, UserId = user.Id, Amount = amount, CreatedAtUtc = at < a.StartsAtUtc ? a.StartsAtUtc.AddMinutes(5) : at });
        }
        a.CurrentPrice = bids.Max(b => b.amount);
        a.BidCount = bids.Length;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Writes a generated placeholder image straight into storage (dev data only — uploads go through FileService validation).</summary>
    private async Task<Guid> SvgAsync(string svg, string name, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var relative = $"seed/{id:N}.svg";
        var full = Path.Combine(files.RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, svg, ct);
        db.Files.Add(new StoredFile { Id = id, Category = FileCategory.Cms, IsPublic = true, OriginalName = name, ContentType = "image/svg+xml", SizeBytes = svg.Length, StoragePath = relative, CreatedAtUtc = now });
        return id;
    }

    private static string Shade(string hex) => hex == "#64748b" ? "#334155" : "#1f2937";
    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static string Lot(string color, string label) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 600"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{color}"/><stop offset="1" stop-color="#0b1f19"/></linearGradient></defs><rect width="800" height="600" fill="url(#g)"/><g fill="none" stroke="#fff" stroke-opacity=".35" stroke-width="10"><path d="M400 170l80 140h-160z"/><path d="M300 360h200l-40 70H340z"/></g><text x="40" y="560" font-family="Segoe UI,Arial" font-size="30" fill="#fff" fill-opacity=".85">{Esc(label)}</text></svg>""";

    private static string Banner(string from, string to) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1600 500"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="{from}"/><stop offset="1" stop-color="{to}"/></linearGradient></defs><rect width="1600" height="500" fill="url(#g)"/><circle cx="1300" cy="250" r="180" fill="#fff" fill-opacity=".08"/><circle cx="1420" cy="120" r="90" fill="#fff" fill-opacity=".08"/></svg>""";

    private static string Monogram(string text, string color) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200"><rect width="200" height="200" rx="40" fill="{color}"/><text x="100" y="122" text-anchor="middle" font-family="Segoe UI,Arial" font-size="72" font-weight="700" fill="#fff">{Esc(text)}</text></svg>""";

    private static string Icon(string color) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><circle cx="32" cy="32" r="30" fill="{color}"/><path d="M32 16l10 17H22zM20 38h24l-5 9H25z" fill="#fff"/></svg>""";
}

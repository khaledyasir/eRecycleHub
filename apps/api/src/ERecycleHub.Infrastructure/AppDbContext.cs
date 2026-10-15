using ERecycleHub.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERecycleHub.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<PublisherApplication> PublisherApplications => Set<PublisherApplication>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionImage> AuctionImages => Set<AuctionImage>();
    public DbSet<AuctionSpec> AuctionSpecs => Set<AuctionSpec>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<WalletLedgerEntry> WalletLedger => Set<WalletLedgerEntry>();
    public DbSet<WishlistItem> Wishlist => Set<WishlistItem>();
    public DbSet<AuctionWatch> AuctionWatches => Set<AuctionWatch>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<ContactInfo> ContactInfos => Set<ContactInfo>();
    public DbSet<AboutSection> AboutSections => Set<AboutSection>();
    public DbSet<HomeHero> HomeHeroes => Set<HomeHero>();
    public DbSet<Benefit> Benefits => Set<Benefit>();
    public DbSet<HowToStep> HowToSteps => Set<HowToStep>();
    public DbSet<FaqCategory> FaqCategories => Set<FaqCategory>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<Slider> Sliders => Set<Slider>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<TranslationOverride> TranslationOverrides => Set<TranslationOverride>();
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<DateTime>().HaveConversion<UtcConverter>().HaveColumnType("datetime2");
        b.Properties<DateTime?>().HaveConversion<NullableUtcConverter>().HaveColumnType("datetime2");
        b.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.PasswordHash).HasMaxLength(1000);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.HasIndex(x => new { x.Role, x.IsActive });
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
            e.ToTable(t => t.HasCheckConstraint("CK_User_Role", "[Role] IN (1,2,3)"));
            FileFk(e, x => x.AvatarFileId);
            FileFk(e, x => x.CrDocumentFileId);
            FileFk(e, x => x.VatCertificateFileId);
            FileFk(e, x => x.IdDocumentFileId);
        });

        m.Entity<PasswordResetToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        m.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Data).HasColumnType("nvarchar(max)");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        m.Entity<Country>(e => FileFk(e, x => x.FlagFileId));

        m.Entity<Category>(e =>
        {
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
            e.HasIndex(x => new { x.CountryId, x.IsActive, x.SortOrder });
            FileFk(e, x => x.IconFileId);
            FileFk(e, x => x.ImageFileId);
        });

        m.Entity<Publisher>(e =>
        {
            e.HasOne(x => x.User).WithOne(x => x.Publisher).HasForeignKey<Publisher>(x => x.UserId);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
            e.Property(x => x.Address).HasMaxLength(1000);
            e.Ignore(x => x.DisplayName);
            FileFk(e, x => x.LogoFileId);
            FileFk(e, x => x.BackgroundFileId);
        });

        m.Entity<PublisherApplication>(e =>
        {
            e.Property(x => x.PasswordHash).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasMany(x => x.Categories).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
        });
        m.Entity<PublisherApplicationCategory>(e =>
        {
            e.HasKey(x => new { x.ApplicationId, x.CategoryId });
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

        m.Entity<Auction>(e =>
        {
            e.HasIndex(x => x.RefNumber).IsUnique();
            e.Property(x => x.RefNumber).HasMaxLength(32);
            foreach (var p in new[] { nameof(Auction.DescEn), nameof(Auction.DescAr), nameof(Auction.ShortDescEn), nameof(Auction.ShortDescAr) })
                e.Property(p).HasColumnType("nvarchar(max)");
            e.Property(x => x.VideoUrl).HasMaxLength(1000);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId);
            e.HasOne(x => x.Publisher).WithMany().HasForeignKey(x => x.PublisherId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Specs).WithOne().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.IsActive, x.CountryId, x.StartsAtUtc, x.EndsAtUtc });
            e.HasIndex(x => x.PublisherId);
            e.HasIndex(x => x.CategoryId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Auction_Prices", "[StartPrice] >= 0 AND [CurrentPrice] >= [StartPrice] AND [BidIncrement] >= 0 AND [InsuranceAmount] >= 0");
                t.HasCheckConstraint("CK_Auction_Dates", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
            });
            FileFk(e, x => x.MainImageFileId);
            FileFk(e, x => x.TermsFileId);
            FileFk(e, x => x.PaymentConditionsFileId);
            FileFk(e, x => x.ContractFileId);
        });

        m.Entity<AuctionImage>(e => e.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId));
        m.Entity<AuctionSpec>(e =>
        {
            e.Property(x => x.ValueEn).HasMaxLength(2000);
            e.Property(x => x.ValueAr).HasMaxLength(2000);
        });

        m.Entity<Bid>(e =>
        {
            e.HasOne(x => x.Auction).WithMany(x => x.Bids).HasForeignKey(x => x.AuctionId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.AuctionId, x.Amount });
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.ToTable(t => t.HasCheckConstraint("CK_Bid_Amount", "[Amount] > 0"));
        });

        m.Entity<WalletTransaction>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.ToTable(t => t.HasCheckConstraint("CK_WalletTransaction_Amount", "[Amount] > 0"));
            FileFk(e, x => x.ProofFileId);
        });

        m.Entity<WalletLedgerEntry>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<WalletTransaction>().WithMany().HasForeignKey(x => x.WalletTransactionId);
            e.HasIndex(x => new { x.UserId, x.Id });
            // One credit and at most one reversal per transaction: a retried approval cannot post twice.
            e.HasIndex(x => new { x.WalletTransactionId, x.Reason }).IsUnique().HasFilter("[WalletTransactionId] IS NOT NULL");
        });

        m.Entity<WishlistItem>(e =>
        {
            e.HasKey(x => new { x.UserId, x.AuctionId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Auction).WithMany().HasForeignKey(x => x.AuctionId);
        });
        m.Entity<AuctionWatch>(e =>
        {
            e.HasKey(x => new { x.UserId, x.AuctionId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Auction).WithMany().HasForeignKey(x => x.AuctionId);
            e.HasIndex(x => x.AuctionId);
        });

        m.Entity<Notification>(e =>
        {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Url).HasMaxLength(1000);
        });

        m.Entity<Review>(e =>
        {
            e.HasOne(x => x.Auction).WithMany().HasForeignKey(x => x.AuctionId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.AuctionId, x.UserId });
            e.Property(x => x.Comment).HasMaxLength(4000);
            e.ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5"));
        });

        m.Entity<ContactMessage>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(4000);
            e.HasIndex(x => x.CreatedAtUtc);
        });

        m.Entity<ContactInfo>(e =>
        {
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
            e.HasIndex(x => x.CountryId);
            e.Property(x => x.MapUrl).HasMaxLength(2000);
            e.Property(x => x.Address).HasMaxLength(1000);
        });

        m.Entity<AboutSection>(e =>
        {
            e.Property(x => x.DescEn).HasColumnType("nvarchar(max)");
            e.Property(x => x.DescAr).HasColumnType("nvarchar(max)");
            FileFk(e, x => x.FirstImageFileId);
            FileFk(e, x => x.SecondImageFileId);
        });
        m.Entity<HomeHero>(e =>
        {
            foreach (var p in new[] { nameof(HomeHero.BadgeEn), nameof(HomeHero.BadgeAr), nameof(HomeHero.PrimaryCtaEn), nameof(HomeHero.PrimaryCtaAr),
                         nameof(HomeHero.SecondaryCtaEn), nameof(HomeHero.SecondaryCtaAr), nameof(HomeHero.CardTitleEn), nameof(HomeHero.CardTitleAr) })
                e.Property(p).HasMaxLength(120);
            e.Property(x => x.TitleEn).HasMaxLength(300);
            e.Property(x => x.TitleAr).HasMaxLength(300);
            e.Property(x => x.LeadEn).HasMaxLength(1000);
            e.Property(x => x.LeadAr).HasMaxLength(1000);
            e.Property(x => x.PrimaryCtaUrl).HasMaxLength(2000);
            e.Property(x => x.SecondaryCtaUrl).HasMaxLength(2000);
            e.Property(x => x.ImageAltEn).HasMaxLength(300);
            e.Property(x => x.ImageAltAr).HasMaxLength(300);
            FileFk(e, x => x.ImageFileId);
        });
        m.Entity<Benefit>(e =>
        {
            e.Property(x => x.DescEn).HasColumnType("nvarchar(max)");
            e.Property(x => x.DescAr).HasColumnType("nvarchar(max)");
            FileFk(e, x => x.IconFileId);
        });
        m.Entity<HowToStep>(e =>
        {
            e.Property(x => x.DescEn).HasColumnType("nvarchar(max)");
            e.Property(x => x.DescAr).HasColumnType("nvarchar(max)");
        });
        m.Entity<FaqCategory>(e => e.HasMany(x => x.Faqs).WithOne().HasForeignKey(x => x.CategoryId));
        m.Entity<Faq>(e =>
        {
            e.Property(x => x.AnswerEn).HasColumnType("nvarchar(max)");
            e.Property(x => x.AnswerAr).HasColumnType("nvarchar(max)");
            e.Property(x => x.QuestionEn).HasMaxLength(1000);
            e.Property(x => x.QuestionAr).HasMaxLength(1000);
        });
        m.Entity<Slider>(e =>
        {
            e.Property(x => x.Link).HasMaxLength(1000);
            e.Property(x => x.Type).HasMaxLength(20);
            FileFk(e, x => x.ImageFileId);
        });
        m.Entity<Announcement>(e =>
        {
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId);
            FileFk(e, x => x.ImageFileId);
        });
        m.Entity<Setting>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.ValueEn).HasColumnType("nvarchar(max)");
            e.Property(x => x.ValueAr).HasColumnType("nvarchar(max)");
        });
        m.Entity<TranslationOverride>(e =>
        {
            e.HasKey(x => new { x.Locale, x.Key });
            e.Property(x => x.Locale).HasMaxLength(5);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.Value).HasMaxLength(4000);
        });
        m.Entity<StoredFile>(e =>
        {
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.LegacyUrl).HasMaxLength(1000);
            e.HasIndex(x => x.LegacyUrl).HasFilter("[LegacyUrl] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId);
        });

        // Nothing cascades except an auction's own images/specs and an application's category picks:
        // deleting money-bearing or referenced rows must always be an explicit decision in code.
        (Type, Type)[] cascades = [(typeof(AuctionImage), typeof(Auction)), (typeof(AuctionSpec), typeof(Auction)), (typeof(PublisherApplicationCategory), typeof(PublisherApplication))];
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            if (!cascades.Contains((fk.DeclaringEntityType.ClrType, fk.PrincipalEntityType.ClrType)))
                fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private static void FileFk<T>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> key) where T : class =>
        e.HasOne<StoredFile>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime(),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}

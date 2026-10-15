namespace ERecycleHub.Domain;

// CMS content managed from the admin dashboard. Field names follow the legacy tables (Ar/En pairs).

public class AboutSection
{
    public int Id { get; set; }
    public string? TitleEn { get; set; }
    public string? TitleAr { get; set; }
    public string? DescEn { get; set; }
    public string? DescAr { get; set; }
    public Guid? FirstImageFileId { get; set; }
    public Guid? SecondImageFileId { get; set; }
    public int? TotalBidders { get; set; }
    public string? GuidelinesVideoUrl { get; set; }
}

/// <summary>The homepage "soft hero" (one record). Empty fields fall back to the texts shipped with the web app; no record = the built-in hero.
/// Titles use a small markup: {text} highlights in green, [text] in purple, a line break starts a new line.</summary>
public class HomeHero
{
    public int Id { get; set; }
    public string? BadgeEn { get; set; }
    public string? BadgeAr { get; set; }
    public string? TitleEn { get; set; }
    public string? TitleAr { get; set; }
    public string? LeadEn { get; set; }
    public string? LeadAr { get; set; }
    public string? PrimaryCtaEn { get; set; }
    public string? PrimaryCtaAr { get; set; }
    public string? PrimaryCtaUrl { get; set; }
    public string? SecondaryCtaEn { get; set; }
    public string? SecondaryCtaAr { get; set; }
    public string? SecondaryCtaUrl { get; set; }
    public string? CardTitleEn { get; set; }
    public string? CardTitleAr { get; set; }
    /// <summary>Null = the picture of the first live auction (the built-in behaviour).</summary>
    public Guid? ImageFileId { get; set; }
    public string? ImageAltEn { get; set; }
    public string? ImageAltAr { get; set; }
    public bool ShowSearch { get; set; } = true;
    /// <summary>When false the homepage shows the built-in hero, so a draft can be prepared without publishing it.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}

public class Benefit
{
    public int Id { get; set; }
    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public string? DescEn { get; set; }
    public string? DescAr { get; set; }
    public Guid? IconFileId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Step of the "How to buy / how to sell" guide (legacy HowToBid).</summary>
public class HowToStep
{
    public int Id { get; set; }
    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public string? DescEn { get; set; }
    public string? DescAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaqCategory
{
    public int Id { get; set; }
    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Faq> Faqs { get; set; } = [];
}

public class Faq
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string QuestionEn { get; set; }
    public required string QuestionAr { get; set; }
    public string? AnswerEn { get; set; }
    public string? AnswerAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Slider
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Link { get; set; }
    public Guid? ImageFileId { get; set; }
    /// <summary>Legacy values "Web" (homepage) or others.</summary>
    public string Type { get; set; } = "Web";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Announcement
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Guid? ImageFileId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ContactInfo
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? MapUrl { get; set; }
    public string? Facebook { get; set; }
    public string? Instagram { get; set; }
    public string? Twitter { get; set; }
    public string? Linkedin { get; set; }
}

public static class SettingKeys
{
    public const string PrivacyPolicy = "privacy-policy";
    public const string Terms = "terms";
}

/// <summary>Bilingual named text (legacy Setting). Legacy rows 3 and 5 are the privacy policy and terms.</summary>
public class Setting
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public string? TitleEn { get; set; }
    public string? TitleAr { get; set; }
    public string? ValueEn { get; set; }
    public string? ValueAr { get; set; }
}

/// <summary>Administrator override of a UI message (replaces editing the legacy .resx files on the server).</summary>
public class TranslationOverride
{
    public required string Locale { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public enum FileCategory
{
    AuctionImage = 1,
    UserAvatar = 2,
    PublisherImage = 3,
    KycDocument = 4,
    TransactionProof = 5,
    Cms = 6,
    /// <summary>Public PDF/Office documents attached to an auction (terms, contract, payment conditions).</summary>
    AuctionDocument = 7,
}

/// <summary>Uploaded file metadata. Bytes live in file storage under <see cref="StoragePath"/>. Private files are only served to their owner and administrators.</summary>
public class StoredFile
{
    public Guid Id { get; set; }
    public FileCategory Category { get; set; }
    public bool IsPublic { get; set; }
    public int? OwnerUserId { get; set; }
    public required string OriginalName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
    /// <summary>Original URL in the legacy system, kept so imported references can be traced.</summary>
    public string? LegacyUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

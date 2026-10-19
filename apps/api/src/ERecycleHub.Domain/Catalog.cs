namespace ERecycleHub.Domain;

public class Country
{
    public int Id { get; set; }
    public required string NameEn { get; set; }
    public required string NameAr { get; set; }
    public string? Code { get; set; }
    public Guid? FlagFileId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>The platform operates in Jordan only: every country list and country check goes through <see cref="Allowed"/>.</summary>
public static class CountryRules
{
    public const string AllowedCode = "JO";
    public static IQueryable<Country> Allowed(this IQueryable<Country> q) => q.Where(c => c.Code == AllowedCode);
}

public class Category
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    public required string TitleEn { get; set; }
    public required string TitleAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? IconFileId { get; set; }
    public Guid? ImageFileId { get; set; }
}

/// <summary>A seller ("store"). Its active flag is the owning user's <see cref="User.IsActive"/>.</summary>
public class Publisher
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    public required string FirstName { get; set; }
    public string LastName { get; set; } = "";
    public string? Address { get; set; }
    public string? SecondMobile { get; set; }
    public Guid? LogoFileId { get; set; }
    public Guid? BackgroundFileId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : FirstName + " " + LastName;
}

public enum ApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// A signed-in customer asking an admin to let them publish auctions. The documents are private files owned by the applicant.
/// At most one application per user can be pending (filtered unique index).
/// </summary>
public class PublisherApplication
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    /// <summary>Store / seller display name.</summary>
    public required string Name { get; set; }
    public string? Company { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public int CountryId { get; set; }
    public Guid? CrDocumentFileId { get; set; }
    public Guid? VatCertificateFileId { get; set; }
    public Guid? IdDocumentFileId { get; set; }
    public ApplicationStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public int? DecidedByUserId { get; set; }
    public List<PublisherApplicationCategory> Categories { get; set; } = [];
}

public class PublisherApplicationCategory
{
    public int ApplicationId { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

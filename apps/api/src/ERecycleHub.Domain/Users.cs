namespace ERecycleHub.Domain;

/// <summary>Numeric values match the legacy Role table (1 Admin, 2 Publisher, 3 Customer) so migrated rows keep their meaning.</summary>
public enum UserRole
{
    Admin = 1,
    Publisher = 2,
    Customer = 3,
}

public class User
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public required string FullName { get; set; }
    /// <summary>Stored lower-cased and trimmed; unique.</summary>
    public required string Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>ASP.NET Core Identity v3 hash, or a legacy "v1.{iter}.{salt}.{hash}" PBKDF2-SHA1 value that is upgraded at the next successful login.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Changes whenever credentials change; signed into the auth cookie so old sessions die.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }

    /// <summary>Projection of the wallet ledger. Only <c>WalletService</c> writes it, always in the same transaction as a ledger entry.</summary>
    public decimal WalletBalance { get; set; }

    public Guid? AvatarFileId { get; set; }
    /// <summary>Shown on the public "Some of our clients" page (legacy showed every active customer's picture; now opt-in by an admin).</summary>
    public bool IsFeaturedClient { get; set; }

    // Company / KYC details (legacy User columns). Documents are private files.
    public string? CompanyName { get; set; }
    public string? CrNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? BankName { get; set; }
    public string? CardNumber { get; set; }
    public string? AccountNumber { get; set; }
    public Guid? CrDocumentFileId { get; set; }
    public Guid? VatCertificateFileId { get; set; }
    public Guid? IdDocumentFileId { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public Publisher? Publisher { get; set; }
}

public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    /// <summary>SHA-256 of the token sent by email; the token itself is never stored.</summary>
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Data { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

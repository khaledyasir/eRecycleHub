namespace ERecycleHub.Domain;

/// <summary>
/// A signed-in mobile device. The bearer token is random; only its SHA-256 is stored. Logout revokes one session; a password
/// change revokes the others; a password reset or account deletion revokes all of them.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
}

/// <summary>Firebase Cloud Messaging registration token of a signed-in device. Unique per token; moves to whoever signs in last.</summary>
public class DeviceToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    /// <summary>The session that registered it, so logout can drop exactly this device's token.</summary>
    public Guid? SessionId { get; set; }
    public required string Token { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// One-time code sent to a phone number or e-mail address. Only the hash is stored. A verified code also proves ownership of the
/// target for a short time, which registration requires.
/// </summary>
public class OtpCode
{
    public long Id { get; set; }
    /// <summary>Normalised phone (07XXXXXXXX) or lower-cased e-mail.</summary>
    public required string Target { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    /// <summary>Set when a verified code has been used to create an account (single use).</summary>
    public DateTime? ConsumedAtUtc { get; set; }
}

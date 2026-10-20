using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace ERecycleHub.Application;

/// <summary>An expected business failure. <see cref="Code"/> is stable and localised by the frontend.</summary>
public sealed class AppException(string code, int status = 400, object? args = null) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public object? Args { get; } = args;

    public static AppException NotFound(string what = "resource") => new($"{what}.not_found", 404);
    public static AppException Forbidden() => new("auth.forbidden", 403);
    public static AppException Conflict(string code, object? args = null) => new(code, 409, args);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public int SafePage => Math.Max(1, Page);
    public int SafeSize => Math.Clamp(PageSize, 1, 100);
    public int Skip => (SafePage - 1) * SafeSize;
}

/// <summary>Who is calling, resolved by the API layer from the auth cookie.</summary>
public sealed record Actor(int UserId, UserRole Role, int? PublisherId = null, string? Ip = null)
{
    public bool IsAdmin => Role == UserRole.Admin;
}

/// <summary>Jordanian mobile numbers: exactly 10 digits, starting with 07, digits only (no spaces or symbols).</summary>
public static class JordanPhone
{
    public static bool IsValid(string? s) => s is { Length: 10 } && s.StartsWith("07", StringComparison.Ordinal) && s.All(char.IsAsciiDigit);

    /// <summary>Required number (sign-up): trims, then must be valid.</summary>
    public static string Require(string? s) => Normalize.Text(s) is { } v && IsValid(v) ? v : throw new AppException("phone.invalid");

    /// <summary>Optional number: null when blank, otherwise must be valid.</summary>
    public static string? Optional(string? s) => Normalize.Text(s) is { } v ? (IsValid(v) ? v : throw new AppException("phone.invalid")) : null;
}

public static class Normalize
{
    public static string Email(string email) => email.Trim().ToLowerInvariant();
    public static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class Audit(AppDbContext db, TimeProvider clock)
{
    /// <summary>Adds an audit row to the current unit of work (saved with the caller's SaveChanges).</summary>
    public void Record(Actor? actor, string action, string entityType, object? entityId, object? data = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor?.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Data = data is null ? null : JsonSerializer.Serialize(data),
            IpAddress = actor?.Ip,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
}

/// <summary>
/// Modern hashes use ASP.NET Core Identity's v3 format (PBKDF2-HMAC-SHA512, 100k iterations, per-hash salt).
/// The legacy app stored "v1.{iterations}.{saltB64}.{hashB64}" (PBKDF2-HMAC-SHA1); those verify once and are re-hashed.
/// Plaintext legacy passwords are hashed by the import tool, so plaintext is never accepted at runtime.
/// </summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string? stored, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(password)) return false;

        if (IsLegacyV1(stored))
        {
            needsRehash = true;
            return VerifyLegacyV1(stored, password);
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(null!, stored, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsLegacyV1(string stored) => stored.StartsWith("v1.", StringComparison.Ordinal) && stored.Split('.').Length == 4;

    private static bool VerifyLegacyV1(string stored, string password)
    {
        var parts = stored.Split('.');
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA1, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string RandomPassword(int length = 16)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, length);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

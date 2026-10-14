using ERecycleHub.Application;

namespace ERecycleHub.UnitTests;

public class PasswordServiceTests
{
    private readonly PasswordService _p = new();

    // Real values written by the legacy application (dev database, dev-only passwords documented in the legacy README).
    private const string LegacyAdminHash = "v1.100000.YGM7pws5plHrwRMAcCEt/A==.d9d8i6xIXDGB/Qd3vomNM/6mzCLHtYx3OttCgq63V8U=";
    private const string LegacyPublisherHash = "v1.100000.J7x2tNIH1jRT63/1042MAg==.q3x9mKMaDzx9/H6Rjd7k/+lj4twUc0LVhlaJpvq+w5k=";

    [Fact]
    public void Modern_hash_round_trips_and_is_not_plaintext()
    {
        var h = _p.Hash("correct horse");
        Assert.DoesNotContain("correct horse", h);
        Assert.True(_p.Verify(h, "correct horse", out var rehash));
        Assert.False(rehash);
    }

    [Fact]
    public void Same_password_hashes_differently() => Assert.NotEqual(_p.Hash("x-password"), _p.Hash("x-password"));

    [Fact]
    public void Wrong_password_rejected() => Assert.False(_p.Verify(_p.Hash("correct horse"), "Correct horse", out _));

    [Fact]
    public void Legacy_v1_hash_from_the_old_system_verifies_and_requests_upgrade()
    {
        Assert.True(_p.Verify(LegacyAdminHash, "AdminPass#2026", out var rehash));
        Assert.True(rehash);
        Assert.True(_p.Verify(LegacyPublisherHash, "PublisherPass#2026", out _));
    }

    [Fact]
    public void Legacy_v1_hash_rejects_wrong_password() => Assert.False(_p.Verify(LegacyAdminHash, "AdminPass#2025", out _));

    [Fact]
    public void Plaintext_is_never_accepted_at_runtime() => Assert.False(_p.Verify("123456", "123456", out _));

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "")]
    [InlineData("v1.100000.!!!.???", "x")]
    [InlineData("v1.abc.AAAA.AAAA", "x")]
    [InlineData("not-a-hash", "not-a-hash")]
    public void Malformed_or_empty_never_verifies_and_never_throws(string? stored, string password) => Assert.False(_p.Verify(stored, password, out _));

    [Fact]
    public void Random_passwords_are_long_and_distinct()
    {
        var a = PasswordService.RandomPassword();
        Assert.True(a.Length >= 16);
        Assert.NotEqual(a, PasswordService.RandomPassword());
    }

    [Fact]
    public void Reset_tokens_are_random_and_only_hashes_are_comparable()
    {
        var t = PasswordService.NewToken();
        Assert.True(t.Length >= 40);
        Assert.Equal(PasswordService.Sha256(t), PasswordService.Sha256(t));
        Assert.NotEqual(PasswordService.Sha256(t), PasswordService.Sha256(PasswordService.NewToken()));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData(null)]
    public void Password_policy_rejects_short_passwords(string? pw) => Assert.Equal("auth.password_policy", Assert.Throws<AppException>(() => AuthService.ValidatePassword(pw)).Code);
}

public class RichTextTests
{
    [Fact]
    public void Keeps_safe_markup_and_removes_script_vectors()
    {
        var html = RichText.Sanitize("<p>kept</p><script>alert(1)</script><img src=x onerror=alert(2)><a href=\"javascript:alert(3)\" onclick=alert(4)>link</a><iframe src=//evil></iframe>")!;
        Assert.Contains("<p>kept</p>", html);
        foreach (var bad in new[] { "alert(1)", "onerror", "javascript:", "onclick", "iframe", "<img" }) Assert.DoesNotContain(bad, html);
    }

    [Fact]
    public void Blank_becomes_null() => Assert.Null(RichText.Sanitize("   "));
}

using Microsoft.EntityFrameworkCore;

namespace ERecycleHub.IntegrationTests;

/// <summary>A code allows MaxAttempts tries (5) however the guesses arrive: parallel wrong guesses cannot share one attempt.</summary>
[Collection(ApiCollection.Name)]
public class OtpAttemptTests(ApiFactory f)
{
    private const string Otp = "12345"; // Otp:FixedCode in ApiFactory

    [Fact]
    public async Task Parallel_wrong_guesses_each_use_an_attempt_and_burn_the_code()
    {
        var c = f.Client();
        var phone = "+962" + ("079" + Random.Shared.Next(1_000_000, 9_999_999))[1..];
        await c.DataAsync(c.PostAsync("/api/v1/auth/otp/send", new { phone }));

        var guesses = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => f.Client().PostAsync("/api/v1/auth/otp/verify", new { phone, code = $"9{i:D4}" }))));
        foreach (var r in guesses) Assert.Equal("INVALID_OTP", await ApiClient.ErrorCodeAsync(r));

        var target = "0" + phone[4..];
        var row = await f.DbAsync(db => db.OtpCodes.AsNoTracking().Where(x => x.Target == target).OrderByDescending(x => x.Id).SingleAsync());
        Assert.Equal(5, row.FailedAttempts);
        Assert.Null(row.VerifiedAtUtc);
        // The right code no longer works: the attempts are used up.
        Assert.Equal("INVALID_OTP", await ApiClient.ErrorCodeAsync(await c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = Otp })));
    }

    [Fact]
    public async Task The_right_code_still_works_after_a_few_wrong_ones()
    {
        var c = f.Client();
        var phone = "+962" + ("079" + Random.Shared.Next(1_000_000, 9_999_999))[1..];
        await c.DataAsync(c.PostAsync("/api/v1/auth/otp/send", new { phone }));
        for (var i = 0; i < 4; i++) await c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = "00000" });
        await c.DataAsync(c.PostAsync("/api/v1/auth/otp/verify", new { phone, code = Otp }));
    }
}

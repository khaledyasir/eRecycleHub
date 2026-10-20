using System.Net;
using ERecycleHub.Application;
using ERecycleHub.Domain;

namespace ERecycleHub.IntegrationTests;

/// <summary>Sign-up rules from QA: Jordanian phone format (07 + 8 digits, digits only) and no spaces in passwords.</summary>
[Collection(ApiCollection.Name)]
public class RegistrationValidationTests(ApiFactory f)
{
    private const string Url = "/api/auth/register";
    private static string NewEmail() => $"reg{Guid.NewGuid():N}@test.local"[..24] + ".local";

    private Task<HttpResponseMessage> RegisterAsync(string phone, string password = "ValidPass#2026") =>
        f.Client().MultipartAsync(HttpMethod.Post, Url, new { fullName = "Reg Tester", email = NewEmail(), password, phone, countryId = 1 });

    [Theory]
    [InlineData("078")]            // too short
    [InlineData("0791234567890")]  // too long
    [InlineData("0612345678")]     // not 07
    [InlineData("07 9123 4567")]   // spaces
    [InlineData("079-123-4567")]   // symbols
    [InlineData("+962791234567")]  // country code
    [InlineData("")]               // required
    public async Task Invalid_phone_numbers_are_rejected(string phone) =>
        Assert.Equal("phone.invalid", await ApiClient.CodeAsync(await RegisterAsync(phone)));

    [Fact]
    public async Task Passwords_with_spaces_are_rejected() =>
        Assert.Equal("auth.password_spaces", await ApiClient.CodeAsync(await RegisterAsync("0791234567", "Valid Pass#2026")));

    [Fact]
    public async Task A_valid_phone_and_password_register() =>
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync("0791234567")).StatusCode);
}

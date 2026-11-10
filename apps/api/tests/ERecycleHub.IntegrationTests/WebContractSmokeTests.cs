using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ERecycleHub.IntegrationTests;

public sealed class WebContractSmokeTests
{
    [Fact]
    public async Task AnonymousWebSessionAndProtectedRoutesKeepTheirContracts()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1,1;Database=unused;User Id=unused;Password=unused;TrustServerCertificate=True;Connect Timeout=1");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        foreach (var path in new[] { "/api/auth/me", "/api/content/settings/{key}", "/api/content/contact-info" })
        {
            var endpoint = Assert.Single(routes, e => e.RoutePattern.RawText == path && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var session = await me.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, session.GetProperty("user").ValueKind);
        Assert.False(string.IsNullOrEmpty(session.GetProperty("csrfToken").GetString()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { email = "test@example.com", password = "test" })).StatusCode);
    }
}

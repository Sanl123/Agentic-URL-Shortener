using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Contracts;
using UrlShortener.IntegrationTests.TestAuth;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Covers JWT authentication and resource-level authorization end-to-end, using deterministic,
/// locally-signed test tokens (see <see cref="TestJwt"/>) — no real identity provider involved.
/// </summary>
public class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseCreated();
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.WithBearerToken(token);
        }

        return client;
    }

    private async Task<string> CreateLinkAsync(HttpClient ownerClient, string targetUrl)
    {
        var response = await ownerClient.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest(targetUrl, null));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        return body!.Code;
    }

    [Fact]
    public async Task Create_WithNoToken_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/no-token", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidSignature_Returns401()
    {
        var tokenSignedWithWrongKey = TestJwt.CreateToken("user-1", signingKey: "a-completely-different-key-that-is-also-long-enough");
        using var client = CreateClient(tokenSignedWithWrongKey);

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/bad-sig", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithWrongIssuer_Returns401()
    {
        var tokenWithWrongIssuer = TestJwt.CreateToken("user-1", issuer: "someone-elses-issuer");
        using var client = CreateClient(tokenWithWrongIssuer);

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/bad-issuer", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithWrongAudience_Returns401()
    {
        var tokenWithWrongAudience = TestJwt.CreateToken("user-1", audience: "someone-elses-audience");
        using var client = CreateClient(tokenWithWrongAudience);

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/bad-audience", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithExpiredToken_Returns401()
    {
        var expiredToken = TestJwt.CreateExpiredToken("user-1");
        using var client = CreateClient(expiredToken);

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/expired", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTokenMissingSubjectClaim_Returns403()
    {
        // Validly signed/issued/not-expired, but no identity to attribute ownership to.
        var tokenWithoutSubject = TestJwt.CreateToken("user-1", includeSubjectClaim: false);
        using var client = CreateClient(tokenWithoutSubject);

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/no-subject", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMetadata_AsOwner_Returns200()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user"));
        var code = await CreateLinkAsync(owner, "https://example.com/owned");

        var response = await owner.GetAsync($"/api/links/{code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetMetadata_AsDifferentUser_Returns404AndDoesNotLeakDetails()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user"));
        var code = await CreateLinkAsync(owner, "https://example.com/private-to-owner");

        using var intruder = CreateClient(TestJwt.CreateToken("intruder-user"));
        var response = await intruder.GetAsync($"/api/links/{code}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-to-owner", body);
        Assert.DoesNotContain("owner-user", body);
    }

    [Fact]
    public async Task Deactivate_AsDifferentUser_Returns404()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user-2"));
        var code = await CreateLinkAsync(owner, "https://example.com/private-deactivate");

        using var intruder = CreateClient(TestJwt.CreateToken("intruder-user-2"));
        var response = await intruder.PostAsync($"/api/links/{code}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetadata_AsAdmin_BypassesOwnershipAndReturns200()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user-3"));
        var code = await CreateLinkAsync(owner, "https://example.com/admin-can-see");

        using var admin = CreateClient(TestJwt.CreateToken("admin-user", isAdmin: true));
        var response = await admin.GetAsync($"/api/links/{code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_AsAdmin_BypassesOwnershipAndSucceeds()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user-4"));
        var code = await CreateLinkAsync(owner, "https://example.com/admin-can-deactivate");

        using var admin = CreateClient(TestJwt.CreateToken("admin-user", isAdmin: true));
        var response = await admin.PostAsync($"/api/links/{code}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_AnonymousNoToken_StillWorks()
    {
        using var owner = CreateClient(TestJwt.CreateToken("owner-user-5"));
        var code = await CreateLinkAsync(owner, "https://example.com/public-redirect");

        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await anonymous.GetAsync($"/{code}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://example.com/public-redirect", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AuthorizationPolicies_AreConfiguredAsSpecified()
    {
        using var scope = _factory.Services.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var regularUser = TestPrincipal.Create("some-user");
        var adminUser = TestPrincipal.Create("some-admin", isAdmin: true);
        var anonymous = TestPrincipal.CreateAnonymous();

        Assert.True((await authorizationService.AuthorizeAsync(regularUser, "AuthenticatedUser")).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(anonymous, "AuthenticatedUser")).Succeeded);

        Assert.True((await authorizationService.AuthorizeAsync(adminUser, "AdminOnly")).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(regularUser, "AdminOnly")).Succeeded);
    }
}

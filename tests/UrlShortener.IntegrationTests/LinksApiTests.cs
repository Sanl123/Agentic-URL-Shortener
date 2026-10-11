using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Contracts;
using UrlShortener.IntegrationTests.TestAuth;

namespace UrlShortener.IntegrationTests;

public class LinksApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LinksApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseCreated();
    }

    private HttpClient CreateAuthenticatedClient(string userId = "user-1") =>
        _factory.CreateClient().WithBearerToken(TestJwt.CreateToken(userId));

    [Fact]
    public async Task Create_WithValidUrl_Returns201WithShortUrl()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/page", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Code));
        Assert.Contains(body.Code, body.ShortUrl);
        Assert.Equal("https://example.com/page", body.OriginalUrl);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task Create_WithInvalidUrl_Returns400ProblemDetails()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("not-a-url", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetMetadata_ForUnknownCode_Returns404()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/links/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RedirectAndMetadata_FullLifecycle_WorksEndToEnd()
    {
        using var client = CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/lifecycle", null));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        // Redirects are anonymous — deliberately no bearer token attached here.
        using var noRedirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirectResponse = await noRedirectClient.GetAsync($"/{created!.Code}");

        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        Assert.Equal("https://example.com/lifecycle", redirectResponse.Headers.Location?.ToString());

        var metadataResponse = await client.GetAsync($"/api/links/{created.Code}");
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ShortLinkMetadataResponse>();

        Assert.Equal(1, metadata!.ClickCount);
        Assert.NotNull(metadata.LastAccessedAtUtc);
    }

    [Fact]
    public async Task Deactivate_ThenRedirect_Returns410Gone()
    {
        using var client = CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/deactivate-me", null));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        var deactivateResponse = await client.PostAsync($"/api/links/{created!.Code}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        using var noRedirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirectResponse = await noRedirectClient.GetAsync($"/{created.Code}");

        Assert.Equal(HttpStatusCode.Gone, redirectResponse.StatusCode);
    }

    [Fact]
    public async Task Deactivate_WhenAlreadyInactive_Returns409()
    {
        using var client = CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest("https://example.com/double-deactivate", null));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        await client.PostAsync($"/api/links/{created!.Code}/deactivate", content: null);

        var secondDeactivate = await client.PostAsync($"/api/links/{created.Code}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.Conflict, secondDeactivate.StatusCode);
    }
}

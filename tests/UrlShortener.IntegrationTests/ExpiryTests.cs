using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Contracts;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.IntegrationTests.TestAuth;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Covers true time-based expiry end-to-end. Domain rules forbid creating an already-expired link
/// (<c>ShortLink.Create</c> requires a future expiry), so these tests create a link with a valid
/// near-future expiry and then advance it into the past directly against the database — the same
/// state the link would eventually reach on its own — to verify the redirect/metadata behavior
/// without an unreliable real-time sleep.
/// </summary>
public class ExpiryTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExpiryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Redirect_ForExpiredLink_Returns410GoneAndDoesNotRecordClick()
    {
        using var client = _factory.CreateClient().WithBearerToken(TestJwt.CreateToken("user-1"));

        var createResponse = await client.PostAsJsonAsync(
            "/api/links",
            new CreateShortLinkApiRequest("https://example.com/expiring", DateTimeOffset.UtcNow.AddMinutes(5)));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        await BackdateExpiryAsync(created!.Code, DateTimeOffset.UtcNow.AddMinutes(-1));

        using var noRedirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirectResponse = await noRedirectClient.GetAsync($"/{created.Code}");

        Assert.Equal(HttpStatusCode.Gone, redirectResponse.StatusCode);

        var metadata = await client.GetFromJsonAsync<ShortLinkMetadataResponse>($"/api/links/{created.Code}");
        Assert.Equal(0, metadata!.ClickCount);
        Assert.True(metadata.IsExpired);
        Assert.True(metadata.IsActive); // expiry and deactivation are independent flags
    }

    [Fact]
    public async Task GetMetadata_ForExpiredButActiveLink_ReportsIsExpiredTrue()
    {
        using var client = _factory.CreateClient().WithBearerToken(TestJwt.CreateToken("user-1"));

        var createResponse = await client.PostAsJsonAsync(
            "/api/links",
            new CreateShortLinkApiRequest("https://example.com/expired-metadata", DateTimeOffset.UtcNow.AddMinutes(5)));
        var created = await createResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        await BackdateExpiryAsync(created!.Code, DateTimeOffset.UtcNow.AddDays(-1));

        var metadata = await client.GetFromJsonAsync<ShortLinkMetadataResponse>($"/api/links/{created.Code}");

        Assert.True(metadata!.IsExpired);
        Assert.True(metadata.IsActive);
    }

    private async Task BackdateExpiryAsync(string code, DateTimeOffset newExpiry)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        var link = await dbContext.ShortLinks.SingleAsync(x => x.Code == code);

        // Simulates the passage of time without an unreliable real-time sleep in the test.
        dbContext.Entry(link).Property("ExpiresAtUtc").CurrentValue = newExpiry;
        await dbContext.SaveChangesAsync();
    }
}

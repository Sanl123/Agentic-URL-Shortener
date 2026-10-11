using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using UrlShortener.IntegrationTests.TestAuth;

namespace UrlShortener.IntegrationTests;

public class RateLimitingTests : IClassFixture<RateLimitedWebApplicationFactory>
{
    private readonly RateLimitedWebApplicationFactory _factory;

    public RateLimitingTests(RateLimitedWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Create_WhenRequestsExceedCreationLimit_Returns429()
    {
        using var client = _factory.CreateClient().WithBearerToken(TestJwt.CreateToken("user-1"));
        var statusCodes = new List<HttpStatusCode>();

        // PermitLimit/4 = 1 permit for the "creation" policy; the second call in the same window must be rejected.
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkApiRequest($"https://example.com/rl-{i}", null));
            statusCodes.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
    }
}

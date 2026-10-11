namespace UrlShortener.IntegrationTests;

/// <summary>Applies a very low creation-endpoint rate limit so the 429 behavior can be exercised quickly.</summary>
public sealed class RateLimitedWebApplicationFactory : TestWebApplicationFactoryBase
{
    protected override void ConfigureAdditionalSettings(IDictionary<string, string?> settings)
    {
        settings["RateLimiting:PermitLimit"] = "4"; // creation policy = PermitLimit / 4 => 1 permit
        settings["RateLimiting:WindowSeconds"] = "60";
        settings["RateLimiting:QueueLimit"] = "0";
    }
}

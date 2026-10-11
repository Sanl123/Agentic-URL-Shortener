namespace UrlShortener.Api.Options;

/// <summary>Bound from the "RateLimiting" configuration section.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
}

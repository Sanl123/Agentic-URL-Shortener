namespace LegacyUrlShortener;

/// <summary>
/// Legacy fixture entity — intentionally has NO expiry concept yet. This is the baseline the
/// brownfield scenario is expected to extend without breaking existing behavior.
/// </summary>
public sealed class ShortLink
{
    public string Code { get; }
    public string OriginalUrl { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public ShortLink(string code, string originalUrl, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            throw new ArgumentException("OriginalUrl is required.", nameof(originalUrl));
        }

        Code = code;
        OriginalUrl = originalUrl;
        CreatedAtUtc = createdAtUtc;
    }
}

namespace UrlShortener.Domain.Entities;

/// <summary>An immutable record of a single redirect against a <see cref="ShortLink"/>.</summary>
public sealed class ClickEvent
{
    public Guid Id { get; private set; }
    public Guid ShortLinkId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Referrer { get; private set; }
    public string? UserAgent { get; private set; }

    private const int MaxFieldLength = 512;

    // EF Core materialization constructor.
    private ClickEvent()
    {
    }

    internal static ClickEvent Create(Guid shortLinkId, DateTimeOffset occurredAtUtc, string? referrer, string? userAgent) =>
        new()
        {
            Id = Guid.NewGuid(),
            ShortLinkId = shortLinkId,
            OccurredAtUtc = occurredAtUtc,
            Referrer = Truncate(referrer),
            UserAgent = Truncate(userAgent)
        };

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, MaxFieldLength)];
}

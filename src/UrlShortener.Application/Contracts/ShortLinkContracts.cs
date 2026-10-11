namespace UrlShortener.Application.Contracts;

public sealed record CreateShortLinkRequest(string OriginalUrl, DateTimeOffset? ExpiresAtUtc);

public sealed record ShortLinkDto(
    string Code,
    string OriginalUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive);

public sealed record ShortLinkMetadataDto(
    string Code,
    string OriginalUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive,
    bool IsExpired,
    int ClickCount,
    DateTimeOffset? LastAccessedAtUtc);

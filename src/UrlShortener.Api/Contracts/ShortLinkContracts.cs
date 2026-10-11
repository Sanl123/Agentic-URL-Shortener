using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Contracts;

public sealed record CreateShortLinkApiRequest(
    [Required(AllowEmptyStrings = false)]
    [MaxLength(2048)]
    string OriginalUrl,
    DateTimeOffset? ExpiresAtUtc);

public sealed record ShortLinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive);

public sealed record ShortLinkMetadataResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive,
    bool IsExpired,
    int ClickCount,
    DateTimeOffset? LastAccessedAtUtc);

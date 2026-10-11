using UrlShortener.Domain.Exceptions;

namespace UrlShortener.Domain.Entities;

/// <summary>
/// Aggregate root representing a shortened URL. Encapsulates the invariants around creation,
/// expiry, (de)activation, and click accounting. Has no dependency on ASP.NET Core or EF Core.
/// </summary>
public sealed class ShortLink
{
    public const int MaxCodeLength = 32;
    public const int MaxOriginalUrlLength = 2048;

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string OriginalUrl { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public int ClickCount { get; private set; }
    public DateTimeOffset? LastAccessedAtUtc { get; private set; }

    private readonly List<ClickEvent> _clickEvents = new();
    public IReadOnlyCollection<ClickEvent> ClickEvents => _clickEvents.AsReadOnly();

    // EF Core materialization constructor.
    private ShortLink()
    {
    }

    public static ShortLink Create(string code, string originalUrl, DateTimeOffset nowUtc, string ownerId, DateTimeOffset? expiresAtUtc = null)
    {
        code = (code ?? string.Empty).Trim();
        originalUrl = (originalUrl ?? string.Empty).Trim();
        ownerId = (ownerId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength)
        {
            throw new ShortLinkValidationException($"Short code must be between 1 and {MaxCodeLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ShortLinkValidationException("Owner id is required.");
        }

        ValidateOriginalUrl(originalUrl);

        if (expiresAtUtc is not null && expiresAtUtc <= nowUtc)
        {
            throw new ShortLinkValidationException("Expiry date must be in the future.");
        }

        return new ShortLink
        {
            Id = Guid.NewGuid(),
            Code = code,
            OriginalUrl = originalUrl,
            OwnerId = ownerId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IsActive = true,
            ClickCount = 0
        };
    }

    /// <summary>True when <paramref name="userId"/> is the identity that created this link.</summary>
    public bool IsOwnedBy(string userId) => !string.IsNullOrEmpty(userId) && string.Equals(OwnerId, userId, StringComparison.Ordinal);

    public static void ValidateOriginalUrl(string originalUrl)
    {
        if (string.IsNullOrWhiteSpace(originalUrl) || originalUrl.Length > MaxOriginalUrlLength)
        {
            throw new ShortLinkValidationException($"Target URL must be between 1 and {MaxOriginalUrlLength} characters.");
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ShortLinkValidationException("Target URL must be an absolute http or https URL.");
        }
    }

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is not null && ExpiresAtUtc <= nowUtc;

    public bool CanRedirect(DateTimeOffset nowUtc) => IsActive && !IsExpired(nowUtc);

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new ShortLinkAlreadyInactiveException($"Short link '{Code}' is already inactive.");
        }

        IsActive = false;
    }

    public ClickEvent RecordClick(DateTimeOffset nowUtc, string? referrer, string? userAgent)
    {
        if (!CanRedirect(nowUtc))
        {
            throw new ShortLinkNotRedirectableException($"Short link '{Code}' is not redirectable (inactive or expired).");
        }

        ClickCount++;
        LastAccessedAtUtc = nowUtc;

        var click = ClickEvent.Create(Id, nowUtc, referrer, userAgent);
        _clickEvents.Add(click);
        return click;
    }
}

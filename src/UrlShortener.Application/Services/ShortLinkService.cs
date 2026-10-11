using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Contracts;
using UrlShortener.Application.Exceptions;
using UrlShortener.Application.Options;
using UrlShortener.Application.Security;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Application.Services;

public sealed class ShortLinkService : IShortLinkService
{
    private readonly IShortLinkRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShortCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private readonly ShortCodeOptions _options;
    private readonly ILogger<ShortLinkService> _logger;

    public ShortLinkService(
        IShortLinkRepository repository,
        IUnitOfWork unitOfWork,
        IShortCodeGenerator codeGenerator,
        IClock clock,
        IOptions<ShortCodeOptions> options,
        ILogger<ShortLinkService> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ShortLinkDto> CreateAsync(CreateShortLinkRequest request, string ownerId, CancellationToken cancellationToken)
    {
        // Validate early so we never spend a generation attempt on a request that can't succeed.
        Domain.Entities.ShortLink.ValidateOriginalUrl(request.OriginalUrl);

        var now = _clock.UtcNow;
        var attempts = 0;

        while (attempts < _options.MaxGenerationAttempts)
        {
            attempts++;
            var code = _codeGenerator.Generate();
            var shortLink = ShortLink.Create(code, request.OriginalUrl, now, ownerId, request.ExpiresAtUtc);

            if (await _repository.TryAddAsync(shortLink, cancellationToken))
            {
                _logger.LogInformation("Created short link {Code} after {Attempts} attempt(s)", code, attempts);
                return ToDto(shortLink);
            }

            _logger.LogWarning("Short code collision on attempt {Attempt} for candidate {Code}", attempts, code);
        }

        throw new ShortCodeGenerationFailedException(attempts);
    }

    public async Task<ShortLinkMetadataDto> GetMetadataAsync(string code, CallerContext caller, CancellationToken cancellationToken)
    {
        var shortLink = await GetOwnedOrThrowAsync(code, caller, cancellationToken);
        var now = _clock.UtcNow;

        return new ShortLinkMetadataDto(
            shortLink.Code,
            shortLink.OriginalUrl,
            shortLink.CreatedAtUtc,
            shortLink.ExpiresAtUtc,
            shortLink.IsActive,
            shortLink.IsExpired(now),
            shortLink.ClickCount,
            shortLink.LastAccessedAtUtc);
    }

    public async Task<string> ResolveAndRecordClickAsync(string code, string? referrer, string? userAgent, CancellationToken cancellationToken)
    {
        var shortLink = await GetOrThrowAsync(code, cancellationToken);

        // Let the domain decide redirectability; this throws ShortLinkStateException when not.
        shortLink.RecordClick(_clock.UtcNow, referrer, userAgent);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Recorded click #{ClickCount} for {Code}", shortLink.ClickCount, code);
        return shortLink.OriginalUrl;
    }

    public async Task DeactivateAsync(string code, CallerContext caller, CancellationToken cancellationToken)
    {
        var shortLink = await GetOwnedOrThrowAsync(code, caller, cancellationToken);
        shortLink.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated short link {Code}", code);
    }

    private async Task<ShortLink> GetOrThrowAsync(string code, CancellationToken cancellationToken) =>
        await _repository.GetByCodeAsync(code, cancellationToken) ?? throw new ShortLinkNotFoundException(code);

    /// <summary>
    /// Resolves a link by code and enforces resource-level ownership. A non-owner, non-admin caller
    /// gets the identical "not found" outcome as a genuinely unknown code — this is deliberate so the
    /// API never discloses whether a given code belongs to another user.
    /// </summary>
    private async Task<ShortLink> GetOwnedOrThrowAsync(string code, CallerContext caller, CancellationToken cancellationToken)
    {
        var shortLink = await GetOrThrowAsync(code, cancellationToken);
        if (!caller.IsAdmin && !shortLink.IsOwnedBy(caller.UserId))
        {
            throw new ShortLinkNotFoundException(code);
        }

        return shortLink;
    }

    private static ShortLinkDto ToDto(ShortLink shortLink) => new(
        shortLink.Code,
        shortLink.OriginalUrl,
        shortLink.CreatedAtUtc,
        shortLink.ExpiresAtUtc,
        shortLink.IsActive);
}

using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UrlShortener.Api.Contracts;
using UrlShortener.Application.Contracts;
using UrlShortener.Application.Security;
using UrlShortener.Application.Services;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("api/links")]
[Authorize(Policy = "AuthenticatedUser")]
public sealed class LinksController : ControllerBase
{
    private readonly IShortLinkService _shortLinkService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<LinksController> _logger;

    public LinksController(IShortLinkService shortLinkService, IAuthorizationService authorizationService, ILogger<LinksController> logger)
    {
        _shortLinkService = shortLinkService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>Creates a new shortened URL with a securely generated, database-unique short code.</summary>
    [HttpPost]
    [EnableRateLimiting("creation")]
    [ProducesResponseType(typeof(ShortLinkResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ShortLinkResponse>> Create([FromBody] CreateShortLinkApiRequest request, CancellationToken cancellationToken)
    {
        if (TryGetOwnerId() is not { } ownerId)
        {
            return Forbid();
        }

        var dto = await _shortLinkService.CreateAsync(new CreateShortLinkRequest(request.OriginalUrl, request.ExpiresAtUtc), ownerId, cancellationToken);
        var response = ToResponse(dto);

        return CreatedAtAction(nameof(GetMetadata), new { code = dto.Code }, response);
    }

    /// <summary>Retrieves metadata (target URL, status, click count) for a short code. Owner or admin only;
    /// a code that exists but belongs to someone else returns 404, identical to an unknown code.</summary>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ShortLinkMetadataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShortLinkMetadataResponse>> GetMetadata(string code, CancellationToken cancellationToken)
    {
        if (await TryBuildCallerContextAsync() is not { } caller)
        {
            return Forbid();
        }

        var dto = await _shortLinkService.GetMetadataAsync(code, caller, cancellationToken);
        return Ok(ToMetadataResponse(dto));
    }

    /// <summary>Deactivates a short link so it no longer redirects. Owner or admin only; see <see cref="GetMetadata"/>
    /// for the not-found-vs-not-yours behavior.</summary>
    [HttpPost("{code}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(string code, CancellationToken cancellationToken)
    {
        if (await TryBuildCallerContextAsync() is not { } caller)
        {
            return Forbid();
        }

        await _shortLinkService.DeactivateAsync(code, caller, cancellationToken);
        return NoContent();
    }

    /// <summary>The validated token's subject claim, or <c>null</c> if absent — a validly authenticated
    /// token that carries no identity cannot be attributed to an owner, so callers must be refused.</summary>
    private string? TryGetOwnerId()
    {
        var ownerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return string.IsNullOrWhiteSpace(ownerId) ? null : ownerId;
    }

    private async Task<CallerContext?> TryBuildCallerContextAsync()
    {
        if (TryGetOwnerId() is not { } ownerId)
        {
            return null;
        }

        var adminResult = await _authorizationService.AuthorizeAsync(User, "AdminOnly");
        return new CallerContext(ownerId, adminResult.Succeeded);
    }

    private ShortLinkResponse ToResponse(ShortLinkDto dto) => new(
        dto.Code,
        BuildShortUrl(dto.Code),
        dto.OriginalUrl,
        dto.CreatedAtUtc,
        dto.ExpiresAtUtc,
        dto.IsActive);

    private ShortLinkMetadataResponse ToMetadataResponse(ShortLinkMetadataDto dto) => new(
        dto.Code,
        BuildShortUrl(dto.Code),
        dto.OriginalUrl,
        dto.CreatedAtUtc,
        dto.ExpiresAtUtc,
        dto.IsActive,
        dto.IsExpired,
        dto.ClickCount,
        dto.LastAccessedAtUtc);

    private string BuildShortUrl(string code) => $"{Request.Scheme}://{Request.Host}/{code}";
}

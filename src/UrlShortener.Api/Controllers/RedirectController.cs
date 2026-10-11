using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>
/// Handles the public redirect hot path. Deliberately independent of the Engineering Orchestrator —
/// nothing here references or depends on orchestration components.
/// </summary>
[ApiController]
public sealed class RedirectController : ControllerBase
{
    private readonly IShortLinkService _shortLinkService;
    private readonly ILogger<RedirectController> _logger;

    public RedirectController(IShortLinkService shortLinkService, ILogger<RedirectController> logger)
    {
        _shortLinkService = shortLinkService;
        _logger = logger;
    }

    [HttpGet("/{code:regex(^[[0-9A-Za-z]]+$)}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> RedirectToOriginal(string code, CancellationToken cancellationToken)
    {
        var referrer = Request.Headers.Referer.ToString() is { Length: > 0 } r ? r : null;
        var userAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

        var originalUrl = await _shortLinkService.ResolveAndRecordClickAsync(code, referrer, userAgent, cancellationToken);

        return Redirect(originalUrl);
    }
}

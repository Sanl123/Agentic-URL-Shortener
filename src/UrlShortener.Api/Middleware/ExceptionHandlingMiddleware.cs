using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Exceptions;
using UrlShortener.Domain.Exceptions;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Translates domain/application exceptions into RFC 7807 problem-details responses without
/// leaking internal details, and logs the full exception server-side in every case.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = Map(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}", context.Request.Method, context.Request.Path, statusCode);
        }

        var isUnexpected = statusCode == StatusCodes.Status500InternalServerError;
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            // Known domain/application exceptions always expose their (safe) message. Unexpected
            // exceptions only expose full details in Development, matching the Swagger UI gate above.
            Detail = isUnexpected
                ? (_environment.IsDevelopment() ? exception.ToString() : "An unexpected error occurred.")
                : exception.Message,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }

    private static (int StatusCode, string Title) Map(Exception exception) => exception switch
    {
        ShortLinkNotFoundException => (StatusCodes.Status404NotFound, "Short link not found"),
        ShortLinkValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
        ShortLinkAlreadyInactiveException => (StatusCodes.Status409Conflict, "Short link already inactive"),
        ShortLinkNotRedirectableException => (StatusCodes.Status410Gone, "Short link no longer available"),
        ShortCodeGenerationFailedException => (StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
    };
}

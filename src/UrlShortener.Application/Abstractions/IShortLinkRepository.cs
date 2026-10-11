using UrlShortener.Domain.Entities;

namespace UrlShortener.Application.Abstractions;

/// <summary>Persistence contract for <see cref="ShortLink"/> aggregates. Implemented in the
/// Infrastructure layer; the Application layer only depends on this interface.</summary>
public interface IShortLinkRepository
{
    Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to insert a brand-new <see cref="ShortLink"/>. Returns <c>false</c> instead of
    /// throwing when the insert fails because of a short-code uniqueness violation, so the
    /// application layer can retry with a newly generated code without depending on EF Core
    /// exception types.
    /// </summary>
    Task<bool> TryAddAsync(ShortLink shortLink, CancellationToken cancellationToken);
}

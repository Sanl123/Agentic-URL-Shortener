namespace UrlShortener.Application.Services;

using UrlShortener.Application.Contracts;
using UrlShortener.Application.Security;

/// <summary>Use cases for creating, reading, redirecting, and deactivating short links.</summary>
public interface IShortLinkService
{
    /// <summary>Creates a link owned by <paramref name="ownerId"/> (the caller's validated identity).</summary>
    Task<ShortLinkDto> CreateAsync(CreateShortLinkRequest request, string ownerId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.ShortLinkNotFoundException"/> both when the code is unknown
    /// and when it belongs to someone other than <paramref name="caller"/> (who isn't an admin) —
    /// deliberately indistinguishable so callers can't enumerate other users' links.</summary>
    Task<ShortLinkMetadataDto> GetMetadataAsync(string code, CallerContext caller, CancellationToken cancellationToken);

    /// <summary>Resolves a code to its original URL and records a click. Public/anonymous — ownership
    /// is intentionally not checked here. Throws
    /// <see cref="Exceptions.ShortLinkNotFoundException"/> if the code is unknown and
    /// <see cref="Domain.Exceptions.ShortLinkStateException"/> if the link is inactive or expired.</summary>
    Task<string> ResolveAndRecordClickAsync(string code, string? referrer, string? userAgent, CancellationToken cancellationToken);

    /// <summary>See <see cref="GetMetadataAsync"/> for the not-found-vs-not-yours behavior.</summary>
    Task DeactivateAsync(string code, CallerContext caller, CancellationToken cancellationToken);
}

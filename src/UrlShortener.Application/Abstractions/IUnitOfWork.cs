namespace UrlShortener.Application.Abstractions;

/// <summary>Persists changes made to tracked aggregates (updates to an existing <see cref="Domain.Entities.ShortLink"/>).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

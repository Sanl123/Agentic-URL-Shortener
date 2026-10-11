using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Entities;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>In-memory stand-in for <see cref="IShortLinkRepository"/> that mimics a database-enforced
/// unique index on <see cref="ShortLink.Code"/> so collision-retry logic can be tested without EF Core.</summary>
public sealed class FakeShortLinkRepository : IShortLinkRepository, IUnitOfWork
{
    private readonly Dictionary<string, ShortLink> _byCode = new();

    public Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_byCode.TryGetValue(code, out var link) ? link : null);

    public Task<bool> TryAddAsync(ShortLink shortLink, CancellationToken cancellationToken)
    {
        if (_byCode.ContainsKey(shortLink.Code))
        {
            return Task.FromResult(false);
        }

        _byCode[shortLink.Code] = shortLink;
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

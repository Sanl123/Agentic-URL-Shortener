using UrlShortener.Application.Abstractions;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Repositories;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly UrlShortenerDbContext _dbContext;

    public EfUnitOfWork(UrlShortenerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

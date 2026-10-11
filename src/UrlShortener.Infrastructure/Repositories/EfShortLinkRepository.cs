using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Repositories;

public sealed class EfShortLinkRepository : IShortLinkRepository
{
    private readonly UrlShortenerDbContext _dbContext;

    public EfShortLinkRepository(UrlShortenerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        _dbContext.ShortLinks
            .Include(x => x.ClickEvents)
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public async Task<bool> TryAddAsync(ShortLink shortLink, CancellationToken cancellationToken)
    {
        await _dbContext.ShortLinks.AddAsync(shortLink, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex.InnerException))
        {
            // The only unique constraint on ShortLinks is the Code index, so this means a collision.
            _dbContext.Entry(shortLink).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(Exception? inner) =>
        inner switch
        {
            Microsoft.Data.SqlClient.SqlException sqlEx => sqlEx.Number is 2601 or 2627,
            not null when inner.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" =>
                inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}

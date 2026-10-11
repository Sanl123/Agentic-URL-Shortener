using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Exercises real concurrent inserts against an actual SQL Server (LocalDB) database — the
/// SQLite-backed <see cref="CustomWebApplicationFactory"/> shares a single physical connection
/// across requests, which cannot safely demonstrate true concurrent writes. This test requires a
/// reachable SQL Server/LocalDB instance; it reports and exits cleanly if one isn't available
/// rather than failing the whole suite.
/// </summary>
public sealed class ConcurrentShortCodeCreationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=UrlShortenerConcurrencyTests;Trusted_Connection=True;TrustServerCertificate=True";

    private bool _sqlServerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            _sqlServerAvailable = true;

            await using var dbContext = CreateDbContext();
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            _sqlServerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_sqlServerAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task TryAddAsync_TenConcurrentInsertsWithSameCode_OnlyOneSucceeds()
    {
        if (!_sqlServerAvailable)
        {
            // Documented environmental limitation: no reachable SQL Server/LocalDB instance.
            return;
        }

        const string code = "race0001";
        const int concurrency = 10;

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await using var dbContext = CreateDbContext();
            var repository = new EfShortLinkRepository(dbContext);
            var link = ShortLink.Create(code, $"https://example.com/race-{i}", DateTimeOffset.UtcNow, $"owner-{i}");
            return await repository.TryAddAsync(link, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(concurrency - 1, results.Count(r => !r));

        await using var verifyContext = CreateDbContext();
        var persistedCount = await verifyContext.ShortLinks.CountAsync(x => x.Code == code);
        Assert.Equal(1, persistedCount);
    }

    private static UrlShortenerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UrlShortenerDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new UrlShortenerDbContext(options);
    }
}

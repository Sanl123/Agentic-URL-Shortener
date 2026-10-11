using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.IntegrationTests.TestAuth;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Swaps the real SQL Server connection for an in-memory SQLite database so integration tests can
/// exercise the full ASP.NET Core pipeline (routing, model binding, rate limiting, exception
/// handling) and real relational uniqueness-constraint enforcement without a live SQL Server.
/// </summary>
public abstract class TestWebApplicationFactoryBase : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                // AddInfrastructure requires a non-empty value; the actual provider is swapped below.
                ["ConnectionStrings:UrlShortenerDb"] = "DataSource=:memory:",
                // Deterministic, test-only JWT config (see TestJwt) — no real identity provider involved.
                ["Jwt:Issuer"] = TestJwt.Issuer,
                ["Jwt:Audience"] = TestJwt.Audience,
                ["Jwt:SigningKey"] = TestJwt.SigningKey,
                ["Jwt:ClockSkewSeconds"] = "5"
            };
            ConfigureAdditionalSettings(settings);
            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<UrlShortenerDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<UrlShortenerDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Override to inject additional configuration (e.g. tighter rate-limit thresholds) for a specific test scenario.</summary>
    protected virtual void ConfigureAdditionalSettings(IDictionary<string, string?> settings)
    {
    }

    /// <summary>Creates the schema for the in-memory database. Must be called before tests run.</summary>
    public void EnsureDatabaseCreated()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        dbContext.Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
    }
}

/// <summary>Default factory using the standard appsettings.Development.json rate-limit/short-code values.</summary>
public sealed class CustomWebApplicationFactory : TestWebApplicationFactoryBase
{
}

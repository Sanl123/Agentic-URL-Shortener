using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Options;
using UrlShortener.Application.Services;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;
using UrlShortener.Infrastructure.Services;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "UrlShortenerDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<UrlShortenerDbContext>(options => options.UseSqlServer(connectionString));

        services.Configure<ShortCodeOptions>(configuration.GetSection(ShortCodeOptions.SectionName));

        services.AddScoped<IShortLinkRepository, EfShortLinkRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IShortCodeGenerator, CryptoShortCodeGenerator>();
        services.AddScoped<IShortLinkService, ShortLinkService>();

        return services;
    }
}

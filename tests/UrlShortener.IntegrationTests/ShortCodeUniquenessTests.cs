using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Entities;

namespace UrlShortener.IntegrationTests;

/// <summary>Proves uniqueness is enforced by the real database unique index, not merely application logic.</summary>
public class ShortCodeUniquenessTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ShortCodeUniquenessTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task TryAddAsync_WithDuplicateCode_SecondInsertIsRejectedByDatabase()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IShortLinkRepository>();

        var now = DateTimeOffset.UtcNow;
        var first = ShortLink.Create("dupe001", "https://example.com/first", now, "owner-1");
        var second = ShortLink.Create("dupe001", "https://example.com/second", now, "owner-2");

        var firstResult = await repository.TryAddAsync(first, CancellationToken.None);
        var secondResult = await repository.TryAddAsync(second, CancellationToken.None);

        Assert.True(firstResult);
        Assert.False(secondResult);
    }
}

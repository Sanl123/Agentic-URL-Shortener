using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Application.Contracts;
using UrlShortener.Application.Exceptions;
using UrlShortener.Application.Options;
using UrlShortener.Application.Security;
using UrlShortener.Application.Services;
using UrlShortener.Domain.Exceptions;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class ShortLinkServiceTests
{
    private const string Owner = "owner-1";
    private const string OtherOwner = "owner-2";
    private static readonly CallerContext OwnerCaller = new(Owner, IsAdmin: false);
    private static readonly CallerContext OtherOwnerCaller = new(OtherOwner, IsAdmin: false);
    private static readonly CallerContext AdminCaller = new("admin-1", IsAdmin: true);

    private static ShortLinkService CreateService(
        FakeShortLinkRepository repository,
        FakeShortCodeGenerator generator,
        FakeClock clock,
        int maxAttempts = 5) =>
        new(
            repository,
            repository,
            generator,
            clock,
            Options.Create(new ShortCodeOptions { MaxGenerationAttempts = maxAttempts }),
            NullLogger<ShortLinkService>.Instance);

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsActiveShortLink()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), new FakeClock());

        var result = await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        Assert.Equal("abc1234", result.Code);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidUrl_ThrowsValidationException()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), new FakeClock());

        await Assert.ThrowsAsync<ShortLinkValidationException>(() =>
            service.CreateAsync(new CreateShortLinkRequest("not-a-url", null), Owner, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WhenFirstCodeCollides_RetriesAndSucceeds()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        await repository.TryAddAsync(
            UrlShortener.Domain.Entities.ShortLink.Create("taken01", "https://existing.example", clock.UtcNow, Owner),
            CancellationToken.None);

        var generator = new FakeShortCodeGenerator("taken01", "fresh01");
        var service = CreateService(repository, generator, clock);

        var result = await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        Assert.Equal("fresh01", result.Code);
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenAllAttemptsCollide_ThrowsGenerationFailed()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        await repository.TryAddAsync(
            UrlShortener.Domain.Entities.ShortLink.Create("dup0001", "https://existing.example", clock.UtcNow, Owner),
            CancellationToken.None);

        var generator = new FakeShortCodeGenerator("dup0001", "dup0001", "dup0001");
        var service = CreateService(repository, generator, clock, maxAttempts: 3);

        await Assert.ThrowsAsync<ShortCodeGenerationFailedException>(() =>
            service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None));
    }

    [Fact]
    public async Task GetMetadataAsync_WhenCodeMissing_ThrowsNotFound()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator(), new FakeClock());

        await Assert.ThrowsAsync<ShortLinkNotFoundException>(() =>
            service.GetMetadataAsync("missing", OwnerCaller, CancellationToken.None));
    }

    [Fact]
    public async Task GetMetadataAsync_AsOwner_Succeeds()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), new FakeClock());
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        var metadata = await service.GetMetadataAsync("abc1234", OwnerCaller, CancellationToken.None);

        Assert.Equal("abc1234", metadata.Code);
    }

    [Fact]
    public async Task GetMetadataAsync_AsDifferentOwner_ThrowsNotFound()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), new FakeClock());
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        // Deliberately the same exception/outcome as "code does not exist" — no disclosure of cross-user existence.
        await Assert.ThrowsAsync<ShortLinkNotFoundException>(() =>
            service.GetMetadataAsync("abc1234", OtherOwnerCaller, CancellationToken.None));
    }

    [Fact]
    public async Task GetMetadataAsync_AsAdmin_BypassesOwnershipCheck()
    {
        var repository = new FakeShortLinkRepository();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), new FakeClock());
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        var metadata = await service.GetMetadataAsync("abc1234", AdminCaller, CancellationToken.None);

        Assert.Equal("abc1234", metadata.Code);
    }

    [Fact]
    public async Task ResolveAndRecordClickAsync_WhenActive_ReturnsOriginalUrlAndIncrementsCount()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        var originalUrl = await service.ResolveAndRecordClickAsync("abc1234", "https://ref.example", "agent", CancellationToken.None);
        var metadata = await service.GetMetadataAsync("abc1234", OwnerCaller, CancellationToken.None);

        Assert.Equal("https://example.com/page", originalUrl);
        Assert.Equal(1, metadata.ClickCount);
    }

    [Fact]
    public async Task ResolveAndRecordClickAsync_IsAnonymousAndIgnoresOwnership()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        // No CallerContext parameter exists for this method — redirects must work for anyone.
        var originalUrl = await service.ResolveAndRecordClickAsync("abc1234", null, null, CancellationToken.None);

        Assert.Equal("https://example.com/page", originalUrl);
    }

    [Fact]
    public async Task ResolveAndRecordClickAsync_WhenDeactivated_ThrowsNotRedirectable()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);
        await service.DeactivateAsync("abc1234", OwnerCaller, CancellationToken.None);

        await Assert.ThrowsAsync<ShortLinkNotRedirectableException>(() =>
            service.ResolveAndRecordClickAsync("abc1234", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateAsync_WhenAlreadyInactive_ThrowsAlreadyInactive()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);
        await service.DeactivateAsync("abc1234", OwnerCaller, CancellationToken.None);

        await Assert.ThrowsAsync<ShortLinkAlreadyInactiveException>(() =>
            service.DeactivateAsync("abc1234", OwnerCaller, CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateAsync_AsDifferentOwner_ThrowsNotFound()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        await Assert.ThrowsAsync<ShortLinkNotFoundException>(() =>
            service.DeactivateAsync("abc1234", OtherOwnerCaller, CancellationToken.None));
    }

    [Fact]
    public async Task DeactivateAsync_AsAdmin_BypassesOwnershipCheck()
    {
        var repository = new FakeShortLinkRepository();
        var clock = new FakeClock();
        var service = CreateService(repository, new FakeShortCodeGenerator("abc1234"), clock);
        await service.CreateAsync(new CreateShortLinkRequest("https://example.com/page", null), Owner, CancellationToken.None);

        await service.DeactivateAsync("abc1234", AdminCaller, CancellationToken.None);

        var metadata = await service.GetMetadataAsync("abc1234", OwnerCaller, CancellationToken.None);
        Assert.False(metadata.IsActive);
    }
}

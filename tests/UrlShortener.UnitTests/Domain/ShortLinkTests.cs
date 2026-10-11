using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Exceptions;

namespace UrlShortener.UnitTests.Domain;

public class ShortLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string OwnerId = "owner-1";

    [Fact]
    public void Create_WithValidData_ProducesActiveLinkWithZeroClicks()
    {
        var link = ShortLink.Create("abc1234", "https://example.com/page", Now, OwnerId);

        Assert.Equal("abc1234", link.Code);
        Assert.Equal("https://example.com/page", link.OriginalUrl);
        Assert.Equal(OwnerId, link.OwnerId);
        Assert.True(link.IsActive);
        Assert.Equal(0, link.ClickCount);
        Assert.Null(link.ExpiresAtUtc);
        Assert.Null(link.LastAccessedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyCode_Throws(string code)
    {
        Assert.Throws<ShortLinkValidationException>(() => ShortLink.Create(code, "https://example.com", Now, OwnerId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyOwnerId_Throws(string ownerId)
    {
        Assert.Throws<ShortLinkValidationException>(() => ShortLink.Create("abc1234", "https://example.com", Now, ownerId));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    public void Create_WithInvalidOriginalUrl_Throws(string url)
    {
        Assert.Throws<ShortLinkValidationException>(() => ShortLink.Create("abc1234", url, Now, OwnerId));
    }

    [Fact]
    public void Create_WithPastExpiry_Throws()
    {
        Assert.Throws<ShortLinkValidationException>(() =>
            ShortLink.Create("abc1234", "https://example.com", Now, OwnerId, Now.AddMinutes(-1)));
    }

    [Fact]
    public void IsOwnedBy_WithMatchingUserId_ReturnsTrue()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);

        Assert.True(link.IsOwnedBy(OwnerId));
        Assert.False(link.IsOwnedBy("someone-else"));
    }

    [Fact]
    public void CanRedirect_WhenActiveAndNotExpired_ReturnsTrue()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId, Now.AddDays(1));

        Assert.True(link.CanRedirect(Now));
    }

    [Fact]
    public void CanRedirect_WhenExpired_ReturnsFalse()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId, Now.AddMinutes(1));

        Assert.False(link.CanRedirect(Now.AddMinutes(2)));
    }

    [Fact]
    public void Deactivate_WhenActive_SetsInactive()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);

        link.Deactivate();

        Assert.False(link.IsActive);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_Throws()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);
        link.Deactivate();

        Assert.Throws<ShortLinkAlreadyInactiveException>(() => link.Deactivate());
    }

    [Fact]
    public void RecordClick_WhenRedirectable_IncrementsCountAndStoresEvent()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);

        link.RecordClick(Now.AddMinutes(5), "https://referrer.example", "test-agent");

        Assert.Equal(1, link.ClickCount);
        Assert.Equal(Now.AddMinutes(5), link.LastAccessedAtUtc);
        Assert.Single(link.ClickEvents);
    }

    [Fact]
    public void RecordClick_CalledMultipleTimes_AccumulatesCountAndEvents()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);

        link.RecordClick(Now.AddMinutes(1), "https://first.example", "agent-1");
        link.RecordClick(Now.AddMinutes(2), "https://second.example", "agent-2");
        link.RecordClick(Now.AddMinutes(3), null, null);

        Assert.Equal(3, link.ClickCount);
        Assert.Equal(Now.AddMinutes(3), link.LastAccessedAtUtc);
        Assert.Equal(3, link.ClickEvents.Count);
    }

    [Fact]
    public void RecordClick_WhenInactive_Throws()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId);
        link.Deactivate();

        Assert.Throws<ShortLinkNotRedirectableException>(() => link.RecordClick(Now, null, null));
    }

    [Fact]
    public void RecordClick_WhenExpired_Throws()
    {
        var link = ShortLink.Create("abc1234", "https://example.com", Now, OwnerId, Now.AddMinutes(1));

        Assert.Throws<ShortLinkNotRedirectableException>(() => link.RecordClick(Now.AddMinutes(2), null, null));
    }
}

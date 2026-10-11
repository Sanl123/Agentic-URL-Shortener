namespace LegacyUrlShortener.Tests;

/// <summary>Baseline behavior that must keep working after the brownfield scenario adds expiry.</summary>
public class LinkStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_ThenTryGet_ReturnsTheSameLink()
    {
        var store = new LinkStore();

        store.Add("abc123", "https://example.com/page", Now);
        var found = store.TryGet("abc123");

        Assert.NotNull(found);
        Assert.Equal("https://example.com/page", found!.OriginalUrl);
    }

    [Fact]
    public void Add_DuplicateCode_Throws()
    {
        var store = new LinkStore();
        store.Add("dup001", "https://example.com/first", Now);

        Assert.Throws<InvalidOperationException>(() => store.Add("dup001", "https://example.com/second", Now));
    }

    [Fact]
    public void TryGet_UnknownCode_ReturnsNull()
    {
        var store = new LinkStore();

        Assert.Null(store.TryGet("doesnotexist"));
    }
}

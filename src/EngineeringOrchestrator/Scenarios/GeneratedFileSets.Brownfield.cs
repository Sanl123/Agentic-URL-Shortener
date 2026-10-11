using System.Text.Json;

namespace EngineeringOrchestrator.Scenarios;

internal static partial class GeneratedFileSets
{
    /// <summary>First attempt at expiry support — contains a deliberately inverted expiry check.
    /// This is a genuine bug (not staged failure data): the condition is backwards, so it fails
    /// real tests for the right reason and gets corrected by the replan step.</summary>
    public static string BrownfieldAddExpiryBuggyFilesJson() => ToWriteFilesJson(new (string, string)[]
    {
        ("LegacyUrlShortener/ShortLink.cs", BrownfieldShortLinkWithExpiry),
        ("LegacyUrlShortener/LinkStore.cs", BrownfieldLinkStoreBuggy),
        ("LegacyUrlShortener.Tests/ExpiryTests.cs", BrownfieldExpiryTests)
    });

    /// <summary>The corrected version applied by the replan/fix task.</summary>
    public static string BrownfieldFixExpiryBugFilesJson() => ToWriteFilesJson(new (string, string)[]
    {
        ("LegacyUrlShortener/LinkStore.cs", BrownfieldLinkStoreFixed)
    });

    private const string BrownfieldShortLinkWithExpiry = """
        namespace LegacyUrlShortener;

        /// <summary>Legacy fixture entity, extended with an optional expiry. Existing 3-argument
        /// construction (no expiry) continues to compile and behave exactly as before.</summary>
        public sealed class ShortLink
        {
            public string Code { get; }
            public string OriginalUrl { get; }
            public DateTimeOffset CreatedAtUtc { get; }
            public DateTimeOffset? ExpiresAtUtc { get; }

            public ShortLink(string code, string originalUrl, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc = null)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new ArgumentException("Code is required.", nameof(code));
                }

                if (string.IsNullOrWhiteSpace(originalUrl))
                {
                    throw new ArgumentException("OriginalUrl is required.", nameof(originalUrl));
                }

                Code = code;
                OriginalUrl = originalUrl;
                CreatedAtUtc = createdAtUtc;
                ExpiresAtUtc = expiresAtUtc;
            }
        }
        """;

    private const string BrownfieldLinkStoreBuggy = """
        using System.Collections.Concurrent;

        namespace LegacyUrlShortener;

        public sealed class LinkStore
        {
            private readonly ConcurrentDictionary<string, ShortLink> _links = new();

            public ShortLink Add(string code, string originalUrl, DateTimeOffset createdAtUtc) =>
                Add(code, originalUrl, createdAtUtc, expiresAtUtc: null);

            public ShortLink Add(string code, string originalUrl, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc)
            {
                var link = new ShortLink(code, originalUrl, createdAtUtc, expiresAtUtc);
                if (!_links.TryAdd(code, link))
                {
                    throw new InvalidOperationException($"Code '{code}' already exists.");
                }

                return link;
            }

            public ShortLink? TryGet(string code) => TryGet(code, DateTimeOffset.UtcNow);

            public ShortLink? TryGet(string code, DateTimeOffset nowUtc)
            {
                if (!_links.TryGetValue(code, out var link))
                {
                    return null;
                }

                // BUG (first attempt): condition is inverted — this treats still-valid (future
                // expiry) links as missing, and already-expired (past expiry) links as present.
                if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value > nowUtc)
                {
                    return null;
                }

                return link;
            }
        }
        """;

    private const string BrownfieldLinkStoreFixed = """
        using System.Collections.Concurrent;

        namespace LegacyUrlShortener;

        public sealed class LinkStore
        {
            private readonly ConcurrentDictionary<string, ShortLink> _links = new();

            public ShortLink Add(string code, string originalUrl, DateTimeOffset createdAtUtc) =>
                Add(code, originalUrl, createdAtUtc, expiresAtUtc: null);

            public ShortLink Add(string code, string originalUrl, DateTimeOffset createdAtUtc, DateTimeOffset? expiresAtUtc)
            {
                var link = new ShortLink(code, originalUrl, createdAtUtc, expiresAtUtc);
                if (!_links.TryAdd(code, link))
                {
                    throw new InvalidOperationException($"Code '{code}' already exists.");
                }

                return link;
            }

            public ShortLink? TryGet(string code) => TryGet(code, DateTimeOffset.UtcNow);

            public ShortLink? TryGet(string code, DateTimeOffset nowUtc)
            {
                if (!_links.TryGetValue(code, out var link))
                {
                    return null;
                }

                // FIXED: a link is expired once its expiry time has passed, not before.
                if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value <= nowUtc)
                {
                    return null;
                }

                return link;
            }
        }
        """;

    private const string BrownfieldExpiryTests = """
        namespace LegacyUrlShortener.Tests;

        /// <summary>New coverage added by the brownfield scenario for the expiry feature.</summary>
        public class ExpiryTests
        {
            private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            [Fact]
            public void TryGet_ExpiredLink_ReturnsNull()
            {
                var store = new LinkStore();
                store.Add("expired1", "https://example.com/expired", Now, Now.AddMinutes(-5));

                var result = store.TryGet("expired1", Now);

                Assert.Null(result);
            }

            [Fact]
            public void TryGet_ValidNonExpiredLink_ReturnsLink()
            {
                var store = new LinkStore();
                store.Add("valid1", "https://example.com/valid", Now, Now.AddMinutes(5));

                var result = store.TryGet("valid1", Now);

                Assert.NotNull(result);
                Assert.Equal("https://example.com/valid", result!.OriginalUrl);
            }

            [Fact]
            public void TryGet_LinkWithNoExpiry_StillResolves_RegressionCheck()
            {
                var store = new LinkStore();
                store.Add("noexpiry1", "https://example.com/no-expiry", Now);

                var result = store.TryGet("noexpiry1", Now);

                Assert.NotNull(result);
            }
        }
        """;
}

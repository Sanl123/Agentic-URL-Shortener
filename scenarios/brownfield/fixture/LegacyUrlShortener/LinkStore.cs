using System.Collections.Concurrent;

namespace LegacyUrlShortener;

/// <summary>In-memory store for the legacy fixture. No expiry awareness yet — that is the
/// brownfield scenario's job to add without breaking <see cref="TryGet"/> for existing links.</summary>
public sealed class LinkStore
{
    private readonly ConcurrentDictionary<string, ShortLink> _links = new();

    public ShortLink Add(string code, string originalUrl, DateTimeOffset createdAtUtc)
    {
        var link = new ShortLink(code, originalUrl, createdAtUtc);
        if (!_links.TryAdd(code, link))
        {
            throw new InvalidOperationException($"Code '{code}' already exists.");
        }

        return link;
    }

    public ShortLink? TryGet(string code) => _links.TryGetValue(code, out var link) ? link : null;
}

using UrlShortener.Application.Abstractions;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Returns a scripted sequence of codes so collision-retry behavior is deterministic in tests.</summary>
public sealed class FakeShortCodeGenerator : IShortCodeGenerator
{
    private readonly Queue<string> _scriptedCodes;

    public FakeShortCodeGenerator(params string[] scriptedCodes)
    {
        _scriptedCodes = new Queue<string>(scriptedCodes);
    }

    public int CallCount { get; private set; }

    public string Generate()
    {
        CallCount++;
        return _scriptedCodes.Count > 0 ? _scriptedCodes.Dequeue() : $"fallback{CallCount}";
    }
}

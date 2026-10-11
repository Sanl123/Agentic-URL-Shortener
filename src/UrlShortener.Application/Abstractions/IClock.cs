namespace UrlShortener.Application.Abstractions;

/// <summary>Abstraction over the system clock so application logic is deterministically testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

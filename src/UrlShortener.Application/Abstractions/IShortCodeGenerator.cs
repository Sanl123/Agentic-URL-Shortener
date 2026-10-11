namespace UrlShortener.Application.Abstractions;

/// <summary>Generates candidate short codes. Implementations must use a cryptographically secure
/// random source; uniqueness is enforced by the database, not by the generator.</summary>
public interface IShortCodeGenerator
{
    string Generate();
}

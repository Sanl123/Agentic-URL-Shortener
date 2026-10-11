namespace UrlShortener.Application.Exceptions;

/// <summary>Thrown when no unique short code could be generated within the configured attempt budget.</summary>
public sealed class ShortCodeGenerationFailedException : ApplicationLayerException
{
    public ShortCodeGenerationFailedException(int attempts)
        : base($"Failed to generate a unique short code after {attempts} attempt(s).")
    {
    }
}

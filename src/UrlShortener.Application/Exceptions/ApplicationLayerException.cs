namespace UrlShortener.Application.Exceptions;

/// <summary>Base type for exceptions raised by the application/use-case layer (as opposed to domain invariants).</summary>
public abstract class ApplicationLayerException : Exception
{
    protected ApplicationLayerException(string message) : base(message)
    {
    }
}

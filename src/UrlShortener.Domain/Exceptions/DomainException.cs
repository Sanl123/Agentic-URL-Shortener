namespace UrlShortener.Domain.Exceptions;

/// <summary>Base type for all exceptions that represent a violated domain invariant.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

namespace UrlShortener.Domain.Exceptions;

/// <summary>Thrown when data supplied to create or modify a <see cref="Entities.ShortLink"/> violates a domain rule.</summary>
public sealed class ShortLinkValidationException : DomainException
{
    public ShortLinkValidationException(string message) : base(message)
    {
    }
}

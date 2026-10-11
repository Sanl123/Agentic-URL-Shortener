namespace UrlShortener.Domain.Exceptions;

/// <summary>Thrown when deactivating a <see cref="Entities.ShortLink"/> that is already inactive.</summary>
public sealed class ShortLinkAlreadyInactiveException : DomainException
{
    public ShortLinkAlreadyInactiveException(string message) : base(message)
    {
    }
}

/// <summary>Thrown when a redirect/click is attempted against a <see cref="Entities.ShortLink"/> that
/// is inactive or expired.</summary>
public sealed class ShortLinkNotRedirectableException : DomainException
{
    public ShortLinkNotRedirectableException(string message) : base(message)
    {
    }
}

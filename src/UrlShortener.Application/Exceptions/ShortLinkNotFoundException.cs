namespace UrlShortener.Application.Exceptions;

/// <summary>Thrown when a short link lookup by code finds no matching record.</summary>
public sealed class ShortLinkNotFoundException : ApplicationLayerException
{
    public string Code { get; }

    public ShortLinkNotFoundException(string code) : base($"No short link found for code '{code}'.")
    {
        Code = code;
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Options;

namespace UrlShortener.Infrastructure.Services;

/// <summary>Generates short codes using a cryptographically secure random source
/// (<see cref="RandomNumberGenerator"/>). Uniqueness is enforced by the database, not here.</summary>
public sealed class CryptoShortCodeGenerator : IShortCodeGenerator
{
    private readonly ShortCodeOptions _options;

    public CryptoShortCodeGenerator(IOptions<ShortCodeOptions> options)
    {
        _options = options.Value;
    }

    public string Generate()
    {
        var alphabet = _options.Alphabet;
        Span<char> buffer = stackalloc char[_options.Length];

        for (var i = 0; i < buffer.Length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(alphabet.Length);
            buffer[i] = alphabet[index];
        }

        return new string(buffer);
    }
}

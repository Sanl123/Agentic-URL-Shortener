namespace UrlShortener.Api.Options;

/// <summary>
/// Bound from the "Jwt" configuration section. <see cref="SigningKey"/> must never be committed —
/// supply it via `dotnet user-secrets` locally or an environment variable / secret manager in
/// production. See docs/decisions.md for the full local-dev setup and production guidance.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 symmetric signing key, UTF-8 encoded. Must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ClockSkewSeconds { get; set; } = 30;
}

namespace UrlShortener.Application.Options;

/// <summary>Bound from the "ShortCode" configuration section.</summary>
public sealed class ShortCodeOptions
{
    public const string SectionName = "ShortCode";

    public int Length { get; set; } = 7;

    /// <summary>Unambiguous alphabet (excludes 0/O/1/l/I by default) used to render generated codes.</summary>
    public string Alphabet { get; set; } = "23456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Maximum number of generate+insert attempts before giving up on a collision.</summary>
    public int MaxGenerationAttempts { get; set; } = 5;
}

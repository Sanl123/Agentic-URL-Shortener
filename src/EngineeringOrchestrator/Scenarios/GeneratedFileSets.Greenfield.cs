using System.Text.Json;

namespace EngineeringOrchestrator.Scenarios;

/// <summary>
/// Hardcoded source file bundles the fake provider "writes" for each scenario task. This is the
/// deterministic stand-in for what a real model would generate — the content is fixed, not
/// synthesized, but it is genuinely compiled and tested by real <c>dotnet</c> commands once written.
/// </summary>
internal static partial class GeneratedFileSets
{
    private sealed record FileEntry(string Path, string Content);

    private static string ToWriteFilesJson(IEnumerable<(string Path, string Content)> files) =>
        JsonSerializer.Serialize(files.Select(f => new FileEntry(f.Path, f.Content)));

    public static string GreenfieldImplementationFilesJson() => ToWriteFilesJson(new (string, string)[]
    {
        ("GeneratedUrlShortener.Core/GeneratedUrlShortener.Core.csproj", CoreCsproj),
        ("GeneratedUrlShortener.Core/ShortLink.cs", CoreShortLink),
        ("GeneratedUrlShortener.Core/LinkStore.cs", CoreLinkStore),
        ("GeneratedUrlShortener.Api/GeneratedUrlShortener.Api.csproj", ApiCsproj),
        ("GeneratedUrlShortener.Api/Program.cs", ApiProgram),
        ("GeneratedUrlShortener.Tests/GeneratedUrlShortener.Tests.csproj", TestsCsproj),
        ("GeneratedUrlShortener.Tests/ShortLinkTests.cs", TestsShortLinkTests)
    });

    private const string CoreCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    private const string CoreShortLink = """
        namespace GeneratedUrlShortener.Core;

        /// <summary>Generated per the greenfield requirements document — mirrors the main solution's
        /// private-setter, factory-validated entity convention in a single, minimal class.</summary>
        public sealed class ShortLink
        {
            public string Code { get; }
            public string OriginalUrl { get; }
            public DateTimeOffset CreatedAtUtc { get; }

            private ShortLink(string code, string originalUrl, DateTimeOffset createdAtUtc)
            {
                Code = code;
                OriginalUrl = originalUrl;
                CreatedAtUtc = createdAtUtc;
            }

            public static ShortLink Create(string code, string originalUrl, DateTimeOffset nowUtc)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new ArgumentException("Code is required.", nameof(code));
                }

                if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException("OriginalUrl must be an absolute http or https URL.", nameof(originalUrl));
                }

                return new ShortLink(code, originalUrl, nowUtc);
            }
        }
        """;

    private const string CoreLinkStore = """
        using System.Collections.Concurrent;
        using System.Security.Cryptography;

        namespace GeneratedUrlShortener.Core;

        public sealed class LinkStore
        {
            private const string Alphabet = "23456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
            private readonly ConcurrentDictionary<string, ShortLink> _links = new();

            public ShortLink CreateLink(string originalUrl, DateTimeOffset nowUtc)
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var code = GenerateCode();
                    var link = ShortLink.Create(code, originalUrl, nowUtc);
                    if (_links.TryAdd(code, link))
                    {
                        return link;
                    }
                }

                throw new InvalidOperationException("Failed to generate a unique code.");
            }

            public ShortLink? TryGet(string code) => _links.TryGetValue(code, out var link) ? link : null;

            private static string GenerateCode()
            {
                Span<char> buffer = stackalloc char[7];
                for (var i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
                }

                return new string(buffer);
            }
        }
        """;

    private const string ApiCsproj = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\GeneratedUrlShortener.Core\GeneratedUrlShortener.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string ApiProgram = """
        using GeneratedUrlShortener.Core;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<LinkStore>();
        var app = builder.Build();

        // AC1/AC3: create a link from a validated absolute http/https URL, or reject it.
        app.MapPost("/links", (CreateLinkRequest request, LinkStore store) =>
        {
            var link = store.CreateLink(request.OriginalUrl, DateTimeOffset.UtcNow);
            return Results.Created($"/links/{link.Code}", new { link.Code, link.OriginalUrl });
        });

        // AC2/AC4: redirect a known code, or report not-found for an unknown one (no exception).
        app.MapGet("/{code}", (string code, LinkStore store) =>
        {
            var link = store.TryGet(code);
            return link is null ? Results.NotFound() : Results.Redirect(link.OriginalUrl);
        });

        app.Run();

        public sealed record CreateLinkRequest(string OriginalUrl);

        /// <summary>Exposed for potential future WebApplicationFactory-based testing.</summary>
        public partial class Program
        {
        }
        """;

    private const string TestsCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <IsPackable>false</IsPackable>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
            <PackageReference Include="xunit" Version="2.5.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
          </ItemGroup>
          <ItemGroup>
            <Using Include="Xunit" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="..\GeneratedUrlShortener.Core\GeneratedUrlShortener.Core.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string TestsShortLinkTests = """
        using GeneratedUrlShortener.Core;

        namespace GeneratedUrlShortener.Tests;

        public class ShortLinkTests
        {
            [Fact]
            public void CreateLink_WithValidUrl_CanBeResolvedByCode()
            {
                var store = new LinkStore();
                var link = store.CreateLink("https://example.com/page", DateTimeOffset.UtcNow);

                var resolved = store.TryGet(link.Code);

                Assert.NotNull(resolved);
                Assert.Equal("https://example.com/page", resolved!.OriginalUrl);
            }

            [Fact]
            public void CreateLink_WithInvalidUrl_Throws()
            {
                var store = new LinkStore();

                Assert.Throws<ArgumentException>(() => store.CreateLink("not-a-url", DateTimeOffset.UtcNow));
            }

            [Fact]
            public void TryGet_UnknownCode_ReturnsNullNotThrow()
            {
                var store = new LinkStore();

                var result = store.TryGet("doesnotexist");

                Assert.Null(result);
            }
        }
        """;
}

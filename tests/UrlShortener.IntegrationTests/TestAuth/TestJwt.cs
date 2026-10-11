using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace UrlShortener.IntegrationTests.TestAuth;

/// <summary>
/// Deterministic, offline JWT issuer for tests — mints tokens signed with a fixed, test-only key
/// that matches what <see cref="TestWebApplicationFactoryBase"/> configures the API to accept.
/// No real identity provider, network call, or production secret is involved.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "urlshortener-tests-issuer";
    public const string Audience = "urlshortener-tests-audience";

    // 44 bytes — comfortably over the 32-byte minimum the API enforces at startup. Test-only; never used in production.
    public const string SigningKey = "test-only-signing-key-do-not-use-in-production-0123456789";

    public const string AdminRole = "Admin";

    public static string CreateToken(
        string userId,
        bool isAdmin = false,
        TimeSpan? lifetime = null,
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        bool includeSubjectClaim = true)
    {
        var claims = new List<Claim>();
        if (includeSubjectClaim)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId));
        }

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(30));
        // Whichever is earlier: normally "now - 1 min" (so the token is immediately valid), but for
        // an already-expired token (negative lifetime) notBefore must also precede expires.
        var notBefore = expires < now ? expires.AddMinutes(-1) : now.AddMinutes(-1);

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>A token that is already expired, for testing lifetime validation.</summary>
    public static string CreateExpiredToken(string userId) =>
        CreateToken(userId, lifetime: TimeSpan.FromMinutes(-10));
}

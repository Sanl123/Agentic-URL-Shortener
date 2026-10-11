using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace UrlShortener.IntegrationTests.TestAuth;

/// <summary>Builds <see cref="ClaimsPrincipal"/> instances for direct, in-process authorization
/// policy evaluation — no HTTP round-trip or token required.</summary>
public static class TestPrincipal
{
    public static ClaimsPrincipal Create(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId) };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, TestJwt.AdminRole));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal CreateAnonymous() => new(new ClaimsIdentity());
}

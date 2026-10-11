namespace UrlShortener.Application.Security;

/// <summary>
/// The authenticated caller's identity, derived by the API layer from validated JWT claims —
/// never from request-body input. <see cref="UserId"/> is the token's subject; <see cref="IsAdmin"/>
/// reflects the "AdminOnly" authorization policy, letting administrators bypass resource ownership.
/// </summary>
public sealed record CallerContext(string UserId, bool IsAdmin);

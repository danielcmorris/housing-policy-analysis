namespace HousingPolicy.Api.Options;

/// <summary>
/// Auth0 authentication. Identity (login, passwords, MFA) lives entirely in
/// Auth0; the API validates its RS256 access tokens and keeps a local users
/// row per account for the role ('admin' | 'member') and disabled flag.
///
/// Enabled=false (the default) leaves every endpoint open exactly as before —
/// flip it on once the Auth0 tenant has: an API registered with the Audience
/// identifier, and the SPA's callback/logout/web origins configured.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Master switch: when false, no token validation is wired up and
    /// the /api/admin gate allows everyone (pre-Auth0 behavior).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Auth0 tenant domain (no scheme).</summary>
    public string Domain { get; set; } = "urbanpolicy.us.auth0.com";

    /// <summary>The SPA application's client id (public, ships to the browser).</summary>
    public string ClientId { get; set; } = "YgU1b1hokXlL7pEMBwL9q1oePPaPoGEe";

    /// <summary>API identifier registered in Auth0 (the token audience).</summary>
    public string Audience { get; set; } = "https://api.urbanpolicy.us";

    /// <summary>This email is always promoted to admin on login — the
    /// bootstrap so the first admin exists without touching the database.</summary>
    public string? SeedAdminEmail { get; set; }
}

namespace SmartSchool.API.Options;

/// <summary>Strongly typed binding for the <c>Jwt</c> configuration section.</summary>
public sealed class JwtSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Issuer written to, and required from, access tokens.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Audience written to, and required from, access tokens.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric HMAC-SHA256 signing key. Must be at least 32 bytes; the application refuses to
    /// start otherwise.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Lifetime of issued access tokens, in minutes.</summary>
    public int AccessTokenExpiryMinutes { get; set; } = 15;

    /// <summary>Lifetime of issued refresh tokens, in days.</summary>
    public int RefreshTokenExpiryDays { get; set; } = 30;
}

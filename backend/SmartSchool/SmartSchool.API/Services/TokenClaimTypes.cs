namespace SmartSchool.API.Services;

/// <summary>Custom JWT claim types issued and validated by SmartSchool.</summary>
public static class TokenClaimTypes
{
    /// <summary>
    /// Carries <see cref="Models.User.SecurityStamp"/> so account-wide security events can
    /// invalidate already-issued access tokens before their natural expiry.
    /// </summary>
    public const string SecurityStamp = "stamp";
}

namespace SmartSchool.API.Options;

/// <summary>Strongly typed binding for the <c>Seed</c> configuration section.</summary>
public sealed class SeedSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Seed";

    /// <summary>The platform admin created on first seed (<c>Seed:PlatformAdmin:*</c>).</summary>
    public PlatformAdminSeed PlatformAdmin { get; set; } = new();

    public sealed class PlatformAdminSeed
    {
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Initial password. Only used when the account does not exist yet; an existing account's
        /// password is never overwritten. Keep it in user secrets, not appsettings.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}

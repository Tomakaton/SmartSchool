namespace SmartSchool.API.Models;

public class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Whether the account has completed activation (and has not been deactivated). Defaults to
    /// <c>false</c> so newly provisioned accounts fail closed until magic-link activation finishes.
    /// </summary>
    public bool IsActivated { get; set; }

    /// <summary>
    /// Random per-user token stamp embedded in access tokens and checked on validation. Rotating it
    /// invalidates already-issued access tokens on account-wide security events.
    /// </summary>
    public string SecurityStamp { get; set; } = NewSecurityStamp();

    public DateTime DateCreated { get; set; }

    public ICollection<RoleAssignment> RoleAssignments { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    public void RotateSecurityStamp() => SecurityStamp = NewSecurityStamp();
}

namespace SmartSchool.API.Models;

/// <summary>
/// A persisted refresh token. Only <see cref="TokenHash"/> is stored; the raw token is returned to
/// the client. Rotation revokes the old row and points it at its replacement through
/// <see cref="ReplacedByTokenId"/>, keeping the same <see cref="FamilyId"/> so reuse of a rotated
/// token revokes the entire session family.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// Identifies the login session chain this token belongs to. Preserved across rotations so
    /// replaying a rotated token can revoke every active token in the family.
    /// </summary>
    public Guid FamilyId { get; init; } = Guid.NewGuid();

    /// <summary>SHA-256 hex digest of the raw token.</summary>
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the token was revoked, or <c>null</c> while it is still usable.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>The token issued in place of this one during rotation, if any.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public RefreshToken? ReplacedByToken { get; set; }

    /// <summary>
    /// Optimistic concurrency token backed by PostgreSQL's <c>xmin</c> system column so parallel
    /// refresh requests cannot both rotate the same token.
    /// </summary>
    public uint Version { get; private set; }
}

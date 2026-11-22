namespace SmartSchool.API.Models;

/// <summary>
/// A tenant of the platform: one school or other educational institution. Every institution-scoped
/// record and role assignment ultimately resolves back to this row.
/// </summary>
public class Institution
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>
    /// Institution type such as <c>HIGH_SCHOOL</c>. Kept as a string because the catalogue is owned
    /// by the platform rather than by this assembly.
    /// </summary>
    public required string Type { get; set; }

    public string? Address { get; set; }

    /// <summary>IANA time zone used to render lesson slots, for example <c>Asia/Jerusalem</c>.</summary>
    public required string TimeZoneId { get; set; }

    public InstitutionStatus Status { get; set; } = InstitutionStatus.Active;

    public DateTime DateCreated { get; set; }
}

/// <summary>Operational state of an institution.</summary>
public enum InstitutionStatus
{
    Active,
    Suspended,
    Archived
}

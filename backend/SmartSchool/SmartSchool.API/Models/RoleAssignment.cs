namespace SmartSchool.API.Models;

/// <summary>
/// Grants a role to a user, optionally within one institution and optionally within a date window.
/// A <c>null</c> <see cref="InstitutionId"/> denotes a platform-wide assignment (for example
/// <c>PLATFORM_ADMIN</c>); <c>null</c> <see cref="ValidTo"/> denotes an assignment with no end date.
/// </summary>
public class RoleAssignment
{
    public long Id { get; init; }

    public Guid UserId { get; init; }
    public User User { get; init; } = null!;

    public long RoleId { get; init; }
    public Role Role { get; init; } = null!;  

    public Guid? InstitutionId { get; init; }
    public Institution? Institution { get; init; }

    public DateOnly ValidFrom { get; init; }
    /// <summary>
    /// Last day the assignment is valid (inclusive). Settable so a removal can end the assignment
    /// (<c>ValidTo = today</c>) instead of deleting the row.
    /// </summary>
    public DateOnly? ValidTo { get; set; }
}

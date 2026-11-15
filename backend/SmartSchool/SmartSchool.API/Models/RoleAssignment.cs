namespace SmartSchool.API.Models;

public class RoleAssignment
{
    public Guid UserId { get; init; }
    public User User { get; init; } = null!;

    public long RoleId { get; init; }
    public Role Role { get; init; } = null!;

    public DateTime AssignedAt { get; init; }
}

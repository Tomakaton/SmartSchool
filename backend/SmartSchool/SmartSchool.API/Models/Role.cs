namespace SmartSchool.API.Models;

public class Role
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }

    public ICollection<RoleAssignment> RoleAssignments { get; set; } = [];
}

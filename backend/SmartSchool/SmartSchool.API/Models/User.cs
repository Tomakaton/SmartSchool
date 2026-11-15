namespace SmartSchool.API.Models;

public class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime DateCreated { get; set; }

    public ICollection<RoleAssignment> RoleAssignments { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.API.Models;

namespace SmartSchool.API.Configurations;

public class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("RoleAssignments");

        builder.HasKey(roleAssignment => new
        {
            roleAssignment.UserId,
            roleAssignment.RoleId
        });

        builder.Property(roleAssignment => roleAssignment.AssignedAt)
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasOne(roleAssignment => roleAssignment.User)
            .WithMany(user => user.RoleAssignments)
            .HasForeignKey(roleAssignment => roleAssignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(roleAssignment => roleAssignment.Role)
            .WithMany(role => role.RoleAssignments)
            .HasForeignKey(roleAssignment => roleAssignment.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

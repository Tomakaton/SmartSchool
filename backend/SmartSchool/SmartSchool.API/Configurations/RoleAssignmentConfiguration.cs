using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.API.Models;

namespace SmartSchool.API.Configurations;

public class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("RoleAssignments");

        builder.HasKey(roleAssignment => roleAssignment.Id);

        builder.Property(roleAssignment => roleAssignment.Id)
            .ValueGeneratedOnAdd();

        builder.Property(roleAssignment => roleAssignment.ValidFrom)
            .IsRequired();

        builder.Property(roleAssignment => roleAssignment.ValidTo);

        // Postgres does not treat NULLs as equal in a unique index, so the tenant and platform
        // variants of the natural key need their own partial index. Both are also restricted to open
        // (ValidTo = NULL) rows so that ending an assignment lets the same user be assigned the same
        // role again later; overlapping historical windows are not enforced here.
        builder.HasIndex(roleAssignment => new
                {
                    roleAssignment.UserId,
                    roleAssignment.RoleId,
                    roleAssignment.InstitutionId
                })
            .IsUnique()
            .HasFilter("\"InstitutionId\" IS NOT NULL AND \"ValidTo\" IS NULL")
            .HasDatabaseName("IX_RoleAssignments_UserId_RoleId_InstitutionId");

        builder.HasIndex(roleAssignment => new
                {
                    roleAssignment.UserId,
                    roleAssignment.RoleId
                })
            .IsUnique()
            .HasFilter("\"InstitutionId\" IS NULL AND \"ValidTo\" IS NULL")
            .HasDatabaseName("IX_RoleAssignments_UserId_RoleId_Platform");

        // Used by the per-request authorization resolver: (user, institution, today).
        builder.HasIndex(roleAssignment => new
                {
                    roleAssignment.UserId,
                    roleAssignment.InstitutionId
                })
            .HasDatabaseName("IX_RoleAssignments_UserId_InstitutionId");

        builder.HasOne(roleAssignment => roleAssignment.User)
            .WithMany(user => user.RoleAssignments)
            .HasForeignKey(roleAssignment => roleAssignment.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(roleAssignment => roleAssignment.Role)
            .WithMany(role => role.RoleAssignments)
            .HasForeignKey(roleAssignment => roleAssignment.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(roleAssignment => roleAssignment.Institution)
            .WithMany()
            .HasForeignKey(roleAssignment => roleAssignment.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

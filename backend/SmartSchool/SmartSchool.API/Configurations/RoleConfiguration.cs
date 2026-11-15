using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.API.Models;

namespace SmartSchool.API.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id)
            .ValueGeneratedOnAdd();

        builder.Property(role => role.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(role => role.Name)
            .IsUnique();

        builder.Property(role => role.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.API.Models;

namespace SmartSchool.API.Configurations;

public class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        builder.ToTable("Institutions", table => table.HasCheckConstraint(
            "CK_Institutions_Status",
            "\"Status\" IN ('ACTIVE', 'SUSPENDED', 'ARCHIVED')"));

        builder.HasKey(institution => institution.Id);

        builder.Property(institution => institution.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(institution => institution.Type)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(institution => institution.Address)
            .HasMaxLength(500);

        builder.Property(institution => institution.TimeZoneId)
            .IsRequired()
            .HasMaxLength(100);

        // Stored as its wire name (ACTIVE, SUSPENDED, ARCHIVED) so SQL matches the API contract.
        // HasConversion<string>() alone would write the C# member name ("Active").
        builder.Property(institution => institution.Status)
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<InstitutionStatus>(value, true))
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(institution => institution.DateCreated)
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(institution => institution.Name);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.API.Models;

namespace SmartSchool.API.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(refreshToken => refreshToken.Id);

        builder.Property(refreshToken => refreshToken.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(refreshToken => refreshToken.TokenHash)
            .IsUnique();

        builder.Property(refreshToken => refreshToken.CreatedAt)
            .IsRequired();

        builder.Property(refreshToken => refreshToken.ExpiresAt)
            .IsRequired();

        builder.HasOne(refreshToken => refreshToken.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(refreshToken => refreshToken.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(refreshToken => refreshToken.ReplacedByToken)
            .WithMany()
            .HasForeignKey(refreshToken => refreshToken.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

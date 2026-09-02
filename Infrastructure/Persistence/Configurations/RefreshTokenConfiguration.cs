using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures refresh token persistence.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(token => token.SellerId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(token => token.JwtId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(token => token.ReplacedByTokenHash)
            .HasMaxLength(128);

        builder.Property(token => token.ReasonRevoked)
            .HasMaxLength(128);

        builder.Property(token => token.ConcurrencyStamp)
            .HasMaxLength(32)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        builder.HasIndex(token => token.SellerId);
        builder.HasIndex(token => token.ExpiresAtUtc);
        builder.HasIndex(token => token.RevokedAtUtc);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(token => token.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

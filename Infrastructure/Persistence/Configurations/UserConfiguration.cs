using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the Identity user table.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(user => user.FullName)
            .HasMaxLength(150);

        builder.Property(user => user.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(user => user.TimeZone)
            .HasMaxLength(100);

        builder.Property(user => user.Language)
            .HasMaxLength(20);

        builder.Property(user => user.IsActive)
            .HasDefaultValue(true);

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();
    }
}

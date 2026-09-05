using APCS.Common.Constants;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures issued authentication tokens.
/// </summary>
public sealed class AuthTokenConfiguration : IEntityTypeConfiguration<AuthToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuthToken> builder)
    {
        builder.ToTable("auth_tokens", table =>
        {
            table.HasCheckConstraint(
                "chk_auth_tokens_type",
                $"token_type IN ('{AuthTokenTypes.PasswordReset}', '{AuthTokenTypes.EmailVerification}', '{AuthTokenTypes.Refresh}')");

            // A refresh token must name the access token it was issued with, so a session can
            // be traced end to end.
            table.HasCheckConstraint(
                "chk_auth_tokens_refresh_jwt_id",
                $"token_type <> '{AuthTokenTypes.Refresh}' OR jwt_id IS NOT NULL");

            table.HasCheckConstraint(
                "chk_auth_tokens_revoked_reason",
                "revoked_at IS NULL OR reason_revoked IS NOT NULL");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(token => token.TokenType)
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(ColumnLengths.TokenHash)
            .IsRequired();

        builder.Property(token => token.JwtId)
            .HasMaxLength(ColumnLengths.Stamp);

        builder.Property(token => token.ReplacedByTokenHash)
            .HasMaxLength(ColumnLengths.TokenHash);

        builder.Property(token => token.ReasonRevoked)
            .HasMaxLength(128);

        builder.Property(token => token.CreatedByIp)
            .HasMaxLength(ColumnLengths.IpAddress);

        builder.Property(token => token.RevokedByIp)
            .HasMaxLength(ColumnLengths.IpAddress);

        builder.Property(token => token.UserAgent)
            .HasColumnType("text");

        builder.Property(token => token.ConcurrencyStamp)
            .HasMaxLength(32)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.TokenType });
        builder.HasIndex(token => token.ExpiresAtUtc);
        builder.HasIndex(token => token.RevokedAtUtc);
        builder.HasIndex(token => token.ReplacedByTokenHash);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures the user profile.
/// </summary>
public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(profile => profile.ShopName)
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(profile => profile.ShopDescription)
            .HasColumnType("text");

        builder.Property(profile => profile.TimeZone)
            .HasColumnName("timezone")
            .HasMaxLength(ColumnLengths.Code)
            .HasDefaultValue("UTC")
            .IsRequired();

        builder.Property(profile => profile.Language)
            .HasMaxLength(10)
            .HasDefaultValue("en")
            .IsRequired();

        builder.Property(profile => profile.ThemePreference)
            .HasMaxLength(ColumnLengths.Code)
            .HasDefaultValue("light");

        builder.Property(profile => profile.NotificationEmailEnabled).HasDefaultValue(true);
        builder.Property(profile => profile.NewsletterSubscribed).HasDefaultValue(false);

        builder.Property(profile => profile.ProfileCompletionPercentage)
            .HasPrecision(5, 2)
            .HasDefaultValue(0m);

        builder.HasIndex(profile => profile.UserId).IsUnique();
    }
}

/// <summary>
/// Configures stored external-service API keys.
/// </summary>
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(key => key.ServiceProvider)
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(key => key.KeyIdentifier)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(key => key.KeyValueEncrypted)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(key => key.KeyLast4)
            .HasMaxLength(4);

        builder.Property(key => key.IsActive).HasDefaultValue(true);
        builder.Property(key => key.UsageCount).HasDefaultValue(0);

        builder.Property(key => key.CreatedByIp)
            .HasMaxLength(ColumnLengths.IpAddress);

        builder.HasIndex(key => key.UserId);
        builder.HasIndex(key => key.ServiceProvider);

        builder.HasOne<User>()
            .WithMany(user => user.ApiKeys)
            .HasForeignKey(key => key.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures immutable audit entries.
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(log => log.ActionType)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(log => log.ResourceType)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(log => log.OldValue).HasColumnType("jsonb");
        builder.Property(log => log.NewValue).HasColumnType("jsonb");
        builder.Property(log => log.IpAddress).HasColumnType("inet");
        builder.Property(log => log.UserAgent).HasColumnType("text");

        builder.HasIndex(log => log.ActorUserId);
        builder.HasIndex(log => new { log.ResourceType, log.ResourceId });
        builder.HasIndex(log => log.CreatedAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

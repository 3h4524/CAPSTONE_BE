using APCS.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the Identity-backed user aggregate.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "chk_users_account_status",
                $"account_status IN ('{AccountStatuses.Active}', '{AccountStatuses.Locked}', '{AccountStatuses.Suspended}', '{AccountStatuses.PendingVerification}')");

            table.HasCheckConstraint(
                "chk_users_has_credential",
                "password_hash IS NOT NULL OR oauth_google_id IS NOT NULL");
        });

        builder.Property(user => user.Email)
            .HasMaxLength(ColumnLengths.Email)
            .IsRequired();

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(ColumnLengths.Email)
            .IsRequired();

        builder.Property(user => user.UserName)
            .HasMaxLength(ColumnLengths.Email)
            .IsRequired();

        builder.Property(user => user.NormalizedUserName)
            .HasMaxLength(ColumnLengths.Email)
            .IsRequired();

        // Nullable: accounts created through Google sign-in never set a password.
        builder.Property(user => user.PasswordHash)
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(user => user.SecurityStamp)
            .HasMaxLength(ColumnLengths.Stamp);

        builder.Property(user => user.ConcurrencyStamp)
            .HasMaxLength(ColumnLengths.Stamp)
            .IsConcurrencyToken();

        builder.Property(user => user.PhoneNumber)
            .HasMaxLength(32);

        builder.Property(user => user.FullName)
            .HasMaxLength(ColumnLengths.Name)
            .IsRequired();

        builder.Property(user => user.OAuthGoogleId)
            .HasColumnName("oauth_google_id")
            .HasMaxLength(ColumnLengths.Name);

        builder.Property(user => user.OAuthProvider)
            .HasColumnName("oauth_provider")
            .HasMaxLength(ColumnLengths.Code);

        builder.Property(user => user.AvatarUrl)
            .HasColumnType("text");

        builder.Property(user => user.AccountStatus)
            .HasMaxLength(ColumnLengths.Code)
            .HasDefaultValue(AccountStatuses.Active)
            .IsRequired();

        builder.Property(user => user.EmailConfirmed)
            .HasColumnName("email_verified")
            .HasDefaultValue(false);

        builder.Property(user => user.EmailVerifiedAtUtc)
            .HasColumnName("email_verified_at");

        builder.Property(user => user.LastLoginAtUtc)
            .HasColumnName("last_login_at");

        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        // Replaces Identity's default EmailIndex/UserNameIndex: filtered so a soft-deleted
        // account releases its email for re-registration.
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ix_users_normalized_email")
            .HasFilter("deleted_at IS NULL")
            .IsUnique();

        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("ix_users_normalized_user_name")
            .HasFilter("deleted_at IS NULL")
            .IsUnique();

        builder.HasIndex(user => user.OAuthGoogleId)
            .HasDatabaseName("ix_users_oauth_google_id")
            .HasFilter("oauth_google_id IS NOT NULL AND deleted_at IS NULL")
            .IsUnique();

        builder.HasIndex(user => user.AccountStatus)
            .HasDatabaseName("ix_users_account_status");

        builder.HasIndex(user => user.DeletedAtUtc)
            .HasDatabaseName("ix_users_deleted_at");

        builder.HasOne(user => user.Profile)
            .WithOne()
            .HasForeignKey<Domain.Entities.UserProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

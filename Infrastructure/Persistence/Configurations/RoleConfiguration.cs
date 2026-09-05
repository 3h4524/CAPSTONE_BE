using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the authorization role and its permission grants.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        // Identity's Name/NormalizedName carry the role code used by [Authorize(Roles = ...)].
        builder.Property(role => role.Name)
            .HasColumnName("code")
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(role => role.NormalizedName)
            .HasColumnName("normalized_code")
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(role => role.DisplayName)
            .HasColumnName("name")
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasColumnType("text");

        builder.Property(role => role.IsSystemRole)
            .HasDefaultValue(true);

        builder.Property(role => role.ConcurrencyStamp)
            .HasMaxLength(ColumnLengths.Stamp)
            .IsConcurrencyToken();

        builder.ConfigureCreationTime();

        builder.HasIndex(role => role.NormalizedName)
            .HasDatabaseName("ix_roles_normalized_code")
            .IsUnique();
    }
}

/// <summary>
/// Configures the user-to-role assignment.
/// </summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");

        builder.Property(userRole => userRole.UserId).HasColumnName("user_id");
        builder.Property(userRole => userRole.RoleId).HasColumnName("role_id");

        builder.Property(userRole => userRole.GrantedAtUtc)
            .HasColumnName("granted_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(userRole => userRole.GrantedBy)
            .HasColumnName("granted_by");

        builder.HasIndex(userRole => userRole.RoleId)
            .HasDatabaseName("ix_user_roles_role_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(userRole => userRole.GrantedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures the audit trail of role grants and revocations.
/// </summary>
public sealed class UserRoleHistoryConfiguration : IEntityTypeConfiguration<UserRoleHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserRoleHistory> builder)
    {
        builder.ToTable("user_role_history", table =>
        {
            table.HasCheckConstraint(
                "chk_user_role_history_action",
                "action IN ('granted', 'revoked')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(history => history.Action)
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasColumnType("text");

        builder.HasIndex(history => history.UserId)
            .HasDatabaseName("ix_user_role_history_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(history => history.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(history => history.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(history => history.PerformedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures the permission catalogue and its role grants.
/// </summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(permission => permission.Code)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(permission => permission.Resource)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(permission => permission.Action)
            .HasMaxLength(ColumnLengths.Code)
            .IsRequired();

        builder.Property(permission => permission.Description)
            .HasColumnType("text");

        builder.HasIndex(permission => permission.Code).IsUnique();
        builder.HasIndex(permission => new { permission.Resource, permission.Action });
    }
}

/// <summary>
/// Configures the role-to-permission grant.
/// </summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(grant => new { grant.RoleId, grant.PermissionId });

        builder.Property(grant => grant.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(grant => grant.PermissionId)
            .HasDatabaseName("ix_role_permissions_permission_id");

        builder.HasOne<Role>()
            .WithMany(role => role.RolePermissions)
            .HasForeignKey(grant => grant.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(grant => grant.Permission)
            .WithMany(permission => permission.RolePermissions)
            .HasForeignKey(grant => grant.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures administrator persistence.
/// </summary>
public sealed class AdminConfiguration : IEntityTypeConfiguration<Admin>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Admin> builder)
    {
        builder.ToTable("admins");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(admin => admin.Email).HasMaxLength(254).IsRequired();
        builder.Property(admin => admin.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(admin => admin.FullName).HasMaxLength(150).IsRequired();
        builder.Property(admin => admin.Role).HasMaxLength(64).IsRequired();
        builder.Property(admin => admin.Permissions).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(admin => admin.IsActive).HasDefaultValue(true);
        builder.Property(admin => admin.LastLoginAtUtc).HasColumnName("last_login_at");

        builder.HasIndex(admin => admin.Email).IsUnique();
        builder.HasIndex(admin => admin.Role);
    }
}

/// <summary>
/// Configures system configuration persistence.
/// </summary>
public sealed class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SystemConfiguration> builder)
    {
        builder.ToTable("system_configurations");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(configuration => configuration.UpdatedAtUtc).HasColumnName("modified_at");
        builder.Property(configuration => configuration.ConfigKey).HasMaxLength(150).IsRequired();
        builder.Property(configuration => configuration.ConfigValue).HasColumnType("text").IsRequired();
        builder.Property(configuration => configuration.Description).HasColumnType("text");
        builder.Property(configuration => configuration.IsActive).HasDefaultValue(true);
        builder.Property(configuration => configuration.LastModifiedByAdminId).HasColumnName("last_modified_by");

        builder.HasIndex(configuration => configuration.ConfigKey).IsUnique();

        builder.HasOne(configuration => configuration.LastModifiedByAdmin)
            .WithMany(admin => admin.ModifiedConfigurations)
            .HasForeignKey(configuration => configuration.LastModifiedByAdminId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures audit log persistence.
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(log => log.ActionType).HasMaxLength(64).IsRequired();
        builder.Property(log => log.ResourceType).HasMaxLength(64).IsRequired();
        builder.Property(log => log.OldValue).HasColumnType("jsonb");
        builder.Property(log => log.NewValue).HasColumnType("jsonb");
        builder.Property(log => log.IpAddress).HasColumnType("inet");
        builder.Property(log => log.UserAgent).HasColumnType("text");

        builder.HasIndex(log => log.SellerId);
        builder.HasIndex(log => new { log.ResourceType, log.ResourceId });
        builder.HasIndex(log => log.CreatedAtUtc).IsDescending();

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(log => log.SellerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(log => log.Admin)
            .WithMany(admin => admin.AuditLogs)
            .HasForeignKey(log => log.AdminId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures system metric persistence.
/// </summary>
public sealed class SystemMetricConfiguration : IEntityTypeConfiguration<SystemMetric>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SystemMetric> builder)
    {
        builder.ToTable("system_metrics");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(metric => metric.MetricType).HasMaxLength(64).IsRequired();
        builder.Property(metric => metric.MetricTimestampUtc).HasColumnName("metric_timestamp").IsRequired();
        builder.Property(metric => metric.Value).HasPrecision(15, 2).IsRequired();
        builder.Property(metric => metric.Dimension).HasColumnType("jsonb");

        builder.HasIndex(metric => new { metric.MetricType, metric.MetricTimestampUtc }).IsDescending(false, true);
    }
}

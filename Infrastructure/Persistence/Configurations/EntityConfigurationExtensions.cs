using APCS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provides shared entity configuration conventions.
/// </summary>
internal static class EntityConfigurationExtensions
{
    /// <summary>
    /// Configures the primary key for an entity whose identifier is assigned in the domain.
    /// </summary>
    /// <remarks>
    /// Identifiers are generated client-side by <see cref="BaseEntity"/> so object graphs can be
    /// wired up before saving, hence <c>ValueGeneratedNever</c>: the database must not overwrite
    /// the value the domain already handed out.
    /// </remarks>
    public static void ConfigureGeneratedId<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : BaseEntity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id)
            .ValueGeneratedNever();
    }

    public static void ConfigureCreationTime<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IHasCreationTime
    {
        builder.Property(entity => entity.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }

    public static void ConfigureModificationTime<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IHasModificationTime
    {
        builder.Property(entity => entity.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }

    public static void ConfigureSoftDelete<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletable
    {
        builder.Property(entity => entity.DeletedAtUtc)
            .HasColumnName("deleted_at");

        builder.HasQueryFilter(entity => entity.DeletedAtUtc == null);
    }
}

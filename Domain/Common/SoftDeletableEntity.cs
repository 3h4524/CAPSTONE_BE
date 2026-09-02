namespace APCS.Domain.Common;

/// <summary>
/// Provides persistence-managed timestamps and logical deletion.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Marks this entity as deleted.
    /// </summary>
    public void Delete(DateTimeOffset deletedAtUtc)
    {
        DeletedAtUtc ??= deletedAtUtc;
    }

    /// <summary>
    /// Restores this entity after a logical deletion.
    /// </summary>
    public void Restore()
    {
        DeletedAtUtc = null;
    }
}

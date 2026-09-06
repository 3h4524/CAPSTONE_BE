namespace APCS.Domain.Common;

/// <summary>
/// Provides a creation timestamp and logical deletion, without modification tracking.
/// </summary>
/// <remarks>
/// Use this for append-mostly entities that are never updated in place but can be
/// soft-deleted. Entities that are also edited should derive from
/// <see cref="SoftDeletableEntity"/> instead, which adds <c>UpdatedAtUtc</c>.
/// </remarks>
public abstract class CreationTrackedSoftDeletableEntity : CreationTrackedEntity, ISoftDeletable
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

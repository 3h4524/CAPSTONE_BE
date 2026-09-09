namespace APCS.Domain.Common;

/// <summary>
/// Provides persistence-managed creation and modification timestamps.
/// </summary>
public abstract class AuditableEntity : CreationTrackedEntity, IHasModificationTime
{
    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}

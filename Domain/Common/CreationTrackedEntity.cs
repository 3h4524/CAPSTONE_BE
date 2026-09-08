namespace APCS.Domain.Common;

/// <summary>
/// Provides an auto-generated identifier and creation timestamp.
/// </summary>
public abstract class CreationTrackedEntity : BaseEntity, IHasCreationTime
{
    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

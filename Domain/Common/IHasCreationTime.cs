namespace APCS.Domain.Common;

/// <summary>
/// Marks an entity whose creation time is managed by persistence.
/// </summary>
public interface IHasCreationTime
{
    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    DateTimeOffset CreatedAtUtc { get; }
}

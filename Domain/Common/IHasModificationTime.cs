namespace APCS.Domain.Common;

/// <summary>
/// Marks an entity whose last modification time is managed by persistence.
/// </summary>
public interface IHasModificationTime
{
    /// <summary>
    /// Gets the UTC last-modification timestamp.
    /// </summary>
    DateTimeOffset UpdatedAtUtc { get; }
}

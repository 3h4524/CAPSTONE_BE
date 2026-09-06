namespace APCS.Domain.Common;

/// <summary>
/// Marks an entity that supports logical deletion.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// Gets the UTC deletion timestamp, or <see langword="null"/> when active.
    /// </summary>
    DateTimeOffset? DeletedAtUtc { get; }
}

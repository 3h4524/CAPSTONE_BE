namespace APCS.Domain.Common;

/// <summary>
/// Provides the base identifier for domain entities.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Gets the entity identifier.
    /// </summary>
    public int Id { get; protected set; }
}

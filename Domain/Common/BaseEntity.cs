namespace APCS.Domain.Common;

/// <summary>
/// Provides the base identifier for domain entities.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Gets the entity identifier.
    /// </summary>
    public Guid Id { get; protected init; } = Guid.NewGuid();
}

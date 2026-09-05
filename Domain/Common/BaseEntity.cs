namespace APCS.Domain.Common;

/// <summary>
/// Provides the base identifier for domain entities.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Gets the entity identifier.
    /// </summary>
    /// <remarks>
    /// Generated client-side so object graphs can be wired up before the first save.
    /// <see cref="Guid.NewGuid"/> produces UUIDv4, which scatters B-tree inserts; move to
    /// <c>Guid.CreateVersion7()</c> once the project targets .NET 9 or later.
    /// </remarks>
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

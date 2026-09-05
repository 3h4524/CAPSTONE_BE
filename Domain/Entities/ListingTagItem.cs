using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a single tag within a listing's tag set.
/// </summary>
/// <remarks>
/// Replaces the former array column, so tags can be indexed, deduplicated per set, and
/// ordered by marketplace position.
/// </remarks>
public sealed class ListingTagItem : CreationTrackedEntity
{
    private ListingTagItem()
    {
    }

    /// <summary>
    /// Gets the owning tag set identifier.
    /// </summary>
    public Guid ListingTagId { get; private set; }

    /// <summary>
    /// Gets the tag as shown to buyers.
    /// </summary>
    public string TagValue { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the normalized tag used for duplicate detection.
    /// </summary>
    public string NormalizedValue { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the tag classification.
    /// </summary>
    public string TagType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the marketplace slot the tag occupies.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// Gets whether the tag came from the model or the seller.
    /// </summary>
    public string Source { get; private set; } = "ai";

    /// <summary>
    /// Gets the owning tag set.
    /// </summary>
    public ListingTag ListingTag { get; private set; } = null!;
}

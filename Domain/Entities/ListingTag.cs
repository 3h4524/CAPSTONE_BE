using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one version of a listing's tag set.
/// </summary>
public sealed class ListingTag : AuditableEntity
{
    private readonly List<ListingTagItem> _items = [];

    private ListingTag()
    {
    }

    /// <summary>
    /// Gets the owning listing content identifier.
    /// </summary>
    public Guid ListingContentId { get; private set; }

    /// <summary>
    /// Gets the number of tags in the set.
    /// </summary>
    public int TagCount { get; private set; }

    /// <summary>
    /// Gets the distribution of tag types as JSON.
    /// </summary>
    public string TagTypeDistribution { get; private set; } = "{}";

    /// <summary>
    /// Gets the SEO score for the tag set.
    /// </summary>
    public decimal SeoScore { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the seller edited the tag set.
    /// </summary>
    public bool IsUserEdited { get; private set; }

    /// <summary>
    /// Gets the version number within the listing.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets the owning listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;

    /// <summary>
    /// Gets the individual tags in the set.
    /// </summary>
    public IReadOnlyCollection<ListingTagItem> Items => _items.AsReadOnly();
}

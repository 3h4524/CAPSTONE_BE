using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a generated listing tag set.
/// </summary>
public sealed class ListingTag : AuditableEntity
{
    private ListingTag()
    {
    }

    /// <summary>
    /// Gets the parent listing content identifier.
    /// </summary>
    public int ListingContentId { get; private set; }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the generated tags.
    /// </summary>
    public string[] GeneratedTags { get; private set; } = [];

    /// <summary>
    /// Gets the number of generated tags.
    /// </summary>
    public int TagCount { get; private set; }

    /// <summary>
    /// Gets tag classification data as JSON.
    /// </summary>
    public string TagTypes { get; private set; } = "{}";

    /// <summary>
    /// Gets the tag set SEO score.
    /// </summary>
    public decimal SeoScore { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the seller edited the tags.
    /// </summary>
    public bool UserEdited { get; private set; }

    /// <summary>
    /// Gets the seller-edited tags.
    /// </summary>
    public string[]? UserEditedVersion { get; private set; }

    /// <summary>
    /// Gets the tag set version number.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets the parent listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

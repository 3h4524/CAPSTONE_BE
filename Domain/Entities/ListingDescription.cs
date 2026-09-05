using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one version of a listing description.
/// </summary>
public sealed class ListingDescription : AuditableEntity
{
    private ListingDescription()
    {
    }

    /// <summary>
    /// Gets the owning listing content identifier.
    /// </summary>
    public Guid ListingContentId { get; private set; }

    /// <summary>
    /// Gets the originally generated description.
    /// </summary>
    public string AiGeneratedDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current description, after any seller edits.
    /// </summary>
    public string CurrentDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the seller edited the description.
    /// </summary>
    public bool IsUserEdited { get; private set; }

    /// <summary>
    /// Gets the word count of the current description.
    /// </summary>
    public int WordCount { get; private set; }

    /// <summary>
    /// Gets which structural sections the description follows, as JSON.
    /// </summary>
    public string StructureFollowed { get; private set; } = "{}";

    /// <summary>
    /// Gets a value indicating whether keyword density is within the target range.
    /// </summary>
    public bool KeywordDensityOptimal { get; private set; }

    /// <summary>
    /// Gets the SEO score for the description.
    /// </summary>
    public decimal SeoScore { get; private set; }

    /// <summary>
    /// Gets the version number within the listing.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets the owning listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

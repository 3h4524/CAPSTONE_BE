using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one version of a listing title.
/// </summary>
/// <remarks>
/// <see cref="CharacterCount"/> and <see cref="IsUserEdited"/> are enforced against
/// <see cref="CurrentTitle"/> by database check constraints, so both must be recomputed
/// whenever the title changes.
/// </remarks>
public sealed class ListingTitle : AuditableEntity
{
    private ListingTitle()
    {
    }

    /// <summary>
    /// Gets the owning listing content identifier.
    /// </summary>
    public Guid ListingContentId { get; private set; }

    /// <summary>
    /// Gets the originally generated title.
    /// </summary>
    public string AiGeneratedTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current title, after any seller edits.
    /// </summary>
    public string CurrentTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the seller edited the title.
    /// </summary>
    public bool IsUserEdited { get; private set; }

    /// <summary>
    /// Gets the character count of the current title.
    /// </summary>
    public int CharacterCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the title contains the primary keyword.
    /// </summary>
    public bool IncludesPrimaryKeyword { get; private set; }

    /// <summary>
    /// Gets the SEO score for the title.
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

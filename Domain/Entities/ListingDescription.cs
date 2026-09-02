using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a generated listing description version.
/// </summary>
public sealed class ListingDescription : AuditableEntity
{
    private ListingDescription()
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
    /// Gets the generated description.
    /// </summary>
    public string GeneratedDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generated description word count.
    /// </summary>
    public int WordCount { get; private set; }

    /// <summary>
    /// Gets structure-compliance data as JSON.
    /// </summary>
    public string StructureFollowed { get; private set; } = "{}";

    /// <summary>
    /// Gets the description SEO score.
    /// </summary>
    public decimal SeoScore { get; private set; }

    /// <summary>
    /// Gets a value indicating whether keyword density is optimal.
    /// </summary>
    public bool KeywordDensityOptimal { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the seller edited the description.
    /// </summary>
    public bool UserEdited { get; private set; }

    /// <summary>
    /// Gets the seller-edited description.
    /// </summary>
    public string? UserEditedVersion { get; private set; }

    /// <summary>
    /// Gets the description version number.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets the parent listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

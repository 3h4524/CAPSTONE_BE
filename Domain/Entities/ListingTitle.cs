using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a generated listing title version.
/// </summary>
public sealed class ListingTitle : AuditableEntity
{
    private ListingTitle()
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
    /// Gets the generated title.
    /// </summary>
    public string GeneratedTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generated title character count.
    /// </summary>
    public int CharacterCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the primary keyword is included.
    /// </summary>
    public bool IncludesPrimaryKeyword { get; private set; }

    /// <summary>
    /// Gets the title SEO score.
    /// </summary>
    public decimal SeoScore { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the seller edited the title.
    /// </summary>
    public bool UserEdited { get; private set; }

    /// <summary>
    /// Gets the seller-edited title version.
    /// </summary>
    public string? UserEditedVersion { get; private set; }

    /// <summary>
    /// Gets the title version number.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets the parent listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

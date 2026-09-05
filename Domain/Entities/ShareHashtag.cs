using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one hashtag attached to a social media post, in a fixed position.
/// </summary>
/// <remarks>
/// Replaces the former array column so hashtags can be ordered and queried.
/// </remarks>
public sealed class ShareHashtag : BaseEntity
{
    private ShareHashtag()
    {
    }

    /// <summary>
    /// Gets the owning share identifier.
    /// </summary>
    public Guid SocialMediaShareId { get; private set; }

    /// <summary>
    /// Gets the hashtag text.
    /// </summary>
    public string Hashtag { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the position within the caption.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// Gets the owning share.
    /// </summary>
    public SocialMediaShare SocialMediaShare { get; private set; } = null!;
}

using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a licensed music track available for promotional videos.
/// </summary>
public sealed class MusicTrack : CreationTrackedEntity, ISoftDeletable
{
    private readonly List<PromoVideo> _promoVideos = [];

    private MusicTrack()
    {
    }

    /// <summary>
    /// Gets the track name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the artist name.
    /// </summary>
    public string ArtistName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the track duration in seconds.
    /// </summary>
    public int DurationSeconds { get; private set; }

    /// <summary>
    /// Gets the music genre.
    /// </summary>
    public string Genre { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the music mood.
    /// </summary>
    public string Mood { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the track is royalty-free.
    /// </summary>
    public bool RoyaltyFree { get; private set; } = true;

    /// <summary>
    /// Gets the track license type.
    /// </summary>
    public string LicenseType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the full audio URL.
    /// </summary>
    public string AudioUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the preview audio URL.
    /// </summary>
    public string? PreviewUrl { get; private set; }

    /// <summary>
    /// Gets waveform data as JSON.
    /// </summary>
    public string? WaveformData { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the track is available.
    /// </summary>
    public bool IsAvailable { get; private set; } = true;

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the promotional videos using this track.
    /// </summary>
    public IReadOnlyCollection<PromoVideo> PromoVideos => _promoVideos.AsReadOnly();
}

using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one ordered scene inside a promotional video.
/// </summary>
/// <remarks>
/// A scene shows exactly one asset: either a design image or a mockup image, never both
/// and never neither.
/// </remarks>
public sealed class PromoVideoScene : CreationTrackedEntity
{
    private PromoVideoScene()
    {
    }

    /// <summary>
    /// Gets the owning promotional video identifier.
    /// </summary>
    public Guid PromoVideoId { get; private set; }

    /// <summary>
    /// Gets the design image shown, when the scene uses a design.
    /// </summary>
    public Guid? DesignImageId { get; private set; }

    /// <summary>
    /// Gets the mockup image shown, when the scene uses a mockup.
    /// </summary>
    public Guid? MockupImageId { get; private set; }

    /// <summary>
    /// Gets the scene order within the video.
    /// </summary>
    public int SceneOrder { get; private set; }

    /// <summary>
    /// Gets the scene duration in seconds.
    /// </summary>
    public decimal DurationSeconds { get; private set; } = 3m;

    /// <summary>
    /// Gets the transition effect into this scene.
    /// </summary>
    public string? TransitionEffect { get; private set; }

    /// <summary>
    /// Gets the scene overlay text.
    /// </summary>
    public string? TextOverlayContent { get; private set; }

    /// <summary>
    /// Gets the owning promotional video.
    /// </summary>
    public PromoVideo PromoVideo { get; private set; } = null!;

    /// <summary>
    /// Gets the design image shown, when the scene uses a design.
    /// </summary>
    public DesignImage? DesignImage { get; private set; }

    /// <summary>
    /// Gets the mockup image shown, when the scene uses a mockup.
    /// </summary>
    public MockupImage? MockupImage { get; private set; }
}

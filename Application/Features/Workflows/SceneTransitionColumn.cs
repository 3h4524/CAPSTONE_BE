namespace APCS.Application.Features.Workflows;

/// <summary>
/// What a scene's transition is written as in <c>promo_video_scenes.transition_effect</c>. The column only allows the
/// words of <c>chk_scene_transition</c>, a shorter list than the transitions a video can have (<c>swipe</c>, <c>flash</c>,
/// <c>whip_pan</c> and <c>blur</c> are not in it): saving one of those made the whole save fail.
/// </summary>
/// <remarks>
/// The transition the render uses is the one in the scene's <c>scene_config</c> (the planned <see cref="ScenePlan"/>), which
/// the media worker reads; this column is a label for the scene and nothing reads it back. So a transition the column has no
/// word for is written as the nearest one it has, and the scene keeps its own in its configuration.
/// </remarks>
public static class SceneTransitionColumn
{
    /// <summary>The words <c>chk_scene_transition</c> allows (migration V1__baseline).</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        { "fade", "cut", "slide", "zoom", "ken_burns", "none" };

    /// <summary>The word to write for a scene's transition: itself when the column allows it, otherwise the nearest.</summary>
    public static string? For(string? transition) => transition switch
    {
        null => null,
        _ when Allowed.Contains(transition) => transition,
        // A sideways move.
        "swipe" or "whip_pan" => "slide",
        // A cross between two images, bright or soft.
        "flash" or "blur" => "fade",
        // Never leave a scene unsaved over a label: an unknown word reads as no transition.
        _ => "none"
    };
}

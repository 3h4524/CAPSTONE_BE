namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>How a model photo made with AI looks: where it is taken, how the people are dressed, how they stand.</summary>
/// <param name="Scene">One of <see cref="ModelPhotoStyles.Scenes"/>; null is the grey studio.</param>
/// <param name="Outfit">One of <see cref="ModelPhotoStyles.Outfits"/>; null is casual.</param>
/// <param name="Pose">One of <see cref="ModelPhotoStyles.Poses"/>; null is standing.</param>
public sealed record ModelPhotoStyleDto(string? Scene = null, string? Outfit = null, string? Pose = null);

/// <summary>The products and styles a model photo can be made with AI in, and how a choice of them is checked.</summary>
public static class ModelPhotoStyles
{
    public const string StudioGrey = "studio_grey";
    public const string Casual = "casual";
    public const string Standing = "standing";

    /// <summary>
    /// The products a person can be shown wearing. A mug, a poster or a phone case is not worn; a tote bag is, but the
    /// generator too seldom draws it whole and face-on for it to be found on the photo.
    /// </summary>
    public static readonly IReadOnlyList<string> ProductTypes = ["tshirt", "hoodie"];

    /// <summary>The studios first: their plain backdrop is quicker to find the people on than a real place.</summary>
    public static readonly IReadOnlyList<string> Scenes = [StudioGrey, "studio_beige", "studio_dark", "street", "cafe", "park"];

    public static readonly IReadOnlyList<string> Outfits = [Casual, "streetwear", "minimal", "sporty"];

    public static readonly IReadOnlyList<string> Poses = [Standing, "walking", "leaning", "seated"];

    /// <summary>
    /// The poses a group can take: leaning or seated side by side, people's garments overlap, and each has to stand
    /// apart to be found on its own.
    /// </summary>
    public static readonly IReadOnlyList<string> GroupPoses = [Standing, "walking"];

    /// <summary>The style with every part filled in and lower-cased; a part left out takes its default.</summary>
    public static ModelPhotoStyleDto Clean(ModelPhotoStyleDto? style) => new(
        Normalize(style?.Scene) ?? StudioGrey,
        Normalize(style?.Outfit) ?? Casual,
        Normalize(style?.Pose) ?? Standing);

    /// <summary>What is wrong with a product and style for a photo of one person or of a group, or null.</summary>
    public static string? Problem(string productType, ModelPhotoStyleDto? style, bool group)
    {
        if (!ProductTypes.Contains(Normalize(productType) ?? ""))
            return "A model photo can be made with AI for a t-shirt or a hoodie.";
        var clean = Clean(style);
        if (!Scenes.Contains(clean.Scene!))
            return "Choose where the photo is taken.";
        if (!Outfits.Contains(clean.Outfit!))
            return "Choose how the people are dressed.";
        if (!Poses.Contains(clean.Pose!))
            return "Choose how the people stand.";
        return group && !GroupPoses.Contains(clean.Pose!) ? "Several people can only be standing or walking." : null;
    }

    /// <summary>Whether the scene is a studio, on a plain backdrop.</summary>
    public static bool IsStudio(string? scene) => (Normalize(scene) ?? StudioGrey).StartsWith("studio_", StringComparison.Ordinal);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

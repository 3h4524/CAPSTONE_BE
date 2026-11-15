using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// The model photo an AI scene that shows a person starts from (<see cref="AiVideoPromptCompiler.RequiresPerson"/>).
/// A batch video run makes one for each design whose scenes need it and that has none: Ideogram dresses the shared
/// library's model photo for its product type in the design's main mock-up (<see cref="LifestyleMockupService"/>).
/// </summary>
/// <remarks>
/// One image per design is enough: every scene with a person uses the design's model photo
/// (<see cref="SceneSourceResolver"/>), so a second one would never be filmed. A design whose scenes are all Standard,
/// or whose AI scenes show no person, gets none.
/// </remarks>
public static class PersonModelPhotos
{
    /// <summary>Whether these settings have an AI scene that shows a person.</summary>
    public static bool Needs(GenerateVideoConfig config) =>
        config.AiScenes?.Any(scene => scene.Mode != "standard" && AiVideoPromptCompiler.RequiresPerson(scene.TemplateCode)) == true;

    /// <summary>Whether the design already has a model photo it can use or that waits for review (a rejected one does not count).</summary>
    public static bool Has(IEnumerable<MockupImage> design) =>
        design.Any(asset => asset.Role == ModelPhotoMockupService.Role && asset.DeletedAt == null && asset.ApprovalStatus != "rejected");

    /// <summary>
    /// The design's mock-ups a model photo can be made from, best first: its main mock-up as the video takes it (a Hero
    /// made from a template), then the others; never a model photo, an AI keyframe or a rejected one.
    /// </summary>
    public static IReadOnlyList<MockupImage> Garments(IEnumerable<MockupImage> design) => design
        .Where(asset => asset.Role != ModelPhotoMockupService.Role && asset.DeletedAt == null && asset.ApprovalStatus != "rejected"
            && AiStudioFrameIdentity.Read(asset.Regions) is null)
        .OrderBy(asset => asset.Role == "Hero" ? 0 : 1)
        .ThenBy(asset => asset.MockupTemplateId is null ? 1 : 0)
        .ThenBy(asset => asset.ApprovalStatus == "approved" ? 0 : 1)
        .ThenBy(asset => asset.VariantKey, StringComparer.Ordinal)
        .ThenBy(asset => asset.CreatedAt)
        .ThenBy(asset => asset.Id)
        .ToArray();

    /// <summary>
    /// The system's model photo for a product type: from the shared library, one person before a group, then by name
    /// so the same one is chosen every time.
    /// </summary>
    public static MockupTemplate? DefaultModel(IEnumerable<MockupTemplate> models, string productType) => models
        .Where(model => model.UserId == null && model.IsActive == true && model.Kind == MockupRules.ModelKind
            && string.Equals(model.ProductType, productType, StringComparison.OrdinalIgnoreCase))
        .OrderBy(model => model.People == ModelPeople.Group ? 1 : 0)
        .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(model => model.Id)
        .FirstOrDefault();
}

namespace APCS.Application.Features.Workflows;

/// <summary>
/// The model photo chosen on the Apply mock-up node for each product type. A batch run then makes one model photo per
/// design of that type by itself, as soon as its mock-ups exist: Ideogram dresses the chosen photo in the design's main
/// mock-up (<see cref="LifestyleMockupService.GenerateForVideoAsync"/>), and the image waits at Mockup Approval like the
/// other mock-ups. A product type without a choice gets none (unless its video has a scene with a person, which takes the
/// system's model photo, <see cref="PersonModelPhotos"/>).
/// </summary>
/// <param name="ModelPhotos">
/// The model photo (a mock-up template of the 'model' kind) per group of product types: <c>apparel</c> for t-shirts and
/// hoodies, the product type itself otherwise (e.g. <c>tote_bag</c>).
/// </param>
public sealed record MockupModelPhotoConfig(IReadOnlyDictionary<string, Guid>? ModelPhotos)
{
    /// <summary>Product types one workflow can choose a model photo for at most.</summary>
    public const int MaximumProductTypes = 10;

    public static readonly MockupModelPhotoConfig None = new((IReadOnlyDictionary<string, Guid>?)null);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasChoices => ModelPhotos is { Count: > 0 };

    /// <summary>
    /// The model photo chosen for <paramref name="productType"/>, or null when there is none. A t-shirt and a hoodie share
    /// one choice, kept under <c>apparel</c> (<see cref="BatchMockups.Common.MockupRules.ModelCategoryOf"/>); a workflow saved
    /// before kept it under the type itself, which is still read, after the shared one.
    /// </summary>
    public Guid? For(string? productType)
    {
        if (productType is null || ModelPhotos is null) return null;
        var category = BatchMockups.Common.MockupRules.ModelCategoryOf(productType);
        return ModelPhotos
            .Where(pair => pair.Value != Guid.Empty && BatchMockups.Common.MockupRules.ModelCategoryOf(pair.Key) == category)
            .OrderBy(pair => string.Equals(pair.Key, category, StringComparison.OrdinalIgnoreCase) ? 0
                : string.Equals(pair.Key, productType, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (Guid?)pair.Value)
            .FirstOrDefault();
    }

    /// <summary>Why the choice cannot be saved, or null when it can.</summary>
    public string? Problem() =>
        ModelPhotos is null ? null
        : ModelPhotos.Count > MaximumProductTypes ? $"Choose model photos for at most {MaximumProductTypes} product types."
        : ModelPhotos.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 50 || pair.Value == Guid.Empty)
            ? "Choose a model photo for each product type in Apply mock-up, or remove it."
        : null;

    /// <summary>The Apply mock-up node's model photos in a workflow definition; none without that node.</summary>
    public static MockupModelPhotoConfig From(WorkflowDefinition definition) =>
        definition.Nodes.FirstOrDefault(node => node.Type == "apply-mockup") is { } node ? WorkflowJson.Config<MockupModelPhotoConfig>(node) : None;
}

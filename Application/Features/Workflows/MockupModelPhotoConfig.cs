namespace APCS.Application.Features.Workflows;

/// <summary>
/// The model photo chosen on the Apply mock-up node for each product type. A batch run then makes one model photo per
/// design of that type by itself, as soon as its mock-ups exist: Ideogram dresses the chosen photo in the design's main
/// mock-up (<see cref="LifestyleMockupService.GenerateForVideoAsync"/>), and the image waits at Mockup Approval like the
/// other mock-ups. A product type without a choice gets none (unless its video has a scene with a person, which takes the
/// system's model photo, <see cref="PersonModelPhotos"/>).
/// </summary>
/// <param name="ModelPhotos">The model photo (a mock-up template of the 'model' kind) per APCS product type, e.g. <c>tshirt</c>.</param>
public sealed record MockupModelPhotoConfig(IReadOnlyDictionary<string, Guid>? ModelPhotos)
{
    /// <summary>Product types one workflow can choose a model photo for at most.</summary>
    public const int MaximumProductTypes = 10;

    public static readonly MockupModelPhotoConfig None = new((IReadOnlyDictionary<string, Guid>?)null);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasChoices => ModelPhotos is { Count: > 0 };

    /// <summary>The model photo chosen for <paramref name="productType"/>, or null when there is none.</summary>
    public Guid? For(string? productType) =>
        productType is null || ModelPhotos is null ? null
        : ModelPhotos.Where(pair => string.Equals(pair.Key, productType, StringComparison.OrdinalIgnoreCase) && pair.Value != Guid.Empty)
            .Select(pair => (Guid?)pair.Value).FirstOrDefault();

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

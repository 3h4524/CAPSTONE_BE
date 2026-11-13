using System.Text.Json;
using APCS.Common.Models;
using APCS.Application.Features.Workflows.Validators;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowGraphValidator(WorkflowCapabilityRegistry registry, GenerateVideoConfigValidator videoValidator)
{
    /// <param name="requireProduct">
    /// False for a batch video run: its videos are for the designs confirmed at Mockup Approval, so Product Input
    /// only needs the batch (its product, when set, is the preview product of the scene editor).
    /// </param>
    public Error? Validate(WorkflowDefinition definition, bool requireProduct = true)
    {
        if (definition.Version != 2 || definition.Edges.Count != definition.Nodes.Count - 1)
            return Error.Validation("A video run needs Product Input → Apply Mockup → Mockup Approval → Generate Video → Review Video → Export ZIP in one line. The design steps may come before Apply Mockup.");
        if (definition.Nodes.Select(x => x.Id).Distinct().Count() != definition.Nodes.Count || definition.Nodes.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 100))
            return Error.Validation("Node IDs must be unique and non-empty.");
        var enabled = registry.Get().Nodes.Where(x => x.Enabled).Select(x => x.Type).ToHashSet();
        if (definition.Nodes.Any(x => !enabled.Contains(x.Type)))
            return new("UnsupportedNode", "This workflow contains a node without a backend executor.", ErrorType.Validation);
        var hasListing = definition.Nodes.Any(x => x.Type == "generate-listing");
        var hasVideo = definition.Nodes.Any(x => x.Type == "generate-video");
        var publishes = definition.Nodes.Any(x => WorkflowCapabilityRegistry.PublishSteps.Contains(x.Type));
        if (!hasListing && !hasVideo && !publishes)
            return Error.Validation("Add a Listing Content, Generate Video or publish node before running the workflow.");
        if (hasListing && !hasVideo && !publishes && definition.Nodes.Any(x => WorkflowCapabilityRegistry.BatchSteps.Contains(x.Type)))
            return Error.Validation("A listing-only workflow currently supports Product Input -> Listing Content.");
        if (hasVideo && WorkflowCapabilityRegistry.Pipeline.Any(type => definition.Nodes.Count(x => x.Type == type) != 1))
            return Error.Validation("Each video MVP node must appear exactly once.");
        // Without a video the publish steps follow Mockup Approval, so the seller still approves what is listed.
        if (!hasVideo && publishes)
        {
            if (definition.Nodes.Any(x => x.Type is "review-video" or "export-zip"))
                return Error.Validation("Review Video and Export ZIP need Generate Video: add it, or remove them to publish without a video.");
            if (WorkflowCapabilityRegistry.PublishPipeline.Any(type => definition.Nodes.Count(x => x.Type == type) != 1))
                return Error.Validation("Publishing without a video needs Product Input → Apply Mockup → Mockup Approval before Push to Printify or Publish to Etsy.");
        }
        if (definition.Nodes.Count(x => x.Type == "product-input") != 1 || definition.Nodes.Count(x => x.Type == "generate-listing") > 1)
            return Error.Validation("Product Input is required once and Listing Content may appear only once.");
        if (WorkflowCapabilityRegistry.BatchSteps.Any(type => definition.Nodes.Count(x => x.Type == type) > 1))
            return Error.Validation("Each design step may appear only once.");
        // Publishing comes last: after Export ZIP of a video run, or after Mockup Approval without a video.
        if (WorkflowCapabilityRegistry.PublishSteps.Any(type => definition.Nodes.Count(x => x.Type == type) > 1))
            return Error.Validation("Each publish step may appear only once.");
        // One line in run order, so neither approval gate can be bypassed and the design steps stay ahead of the mock-ups.
        // Listing Content is written from the product's own details and waits for no approval: it may hang off
        // any step, or sit between two of them, without being one of the steps the line is checked against.
        var listing = definition.Nodes.SingleOrDefault(x => x.Type == "generate-listing");
        if (listing is not null && definition.Edges.Count(e => e.Target == listing.Id) != 1)
            return Error.Validation("Connect Listing Content after exactly one step.");
        var ordered = definition.Nodes.Where(x => x != listing).OrderBy(x => Array.IndexOf(WorkflowCapabilityRegistry.RunOrder, x.Type)).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            var direct = definition.Edges.Count(e => e.Source == ordered[i - 1].Id && e.Target == ordered[i].Id);
            var throughListing = listing is not null && definition.Edges.Any(e => e.Source == ordered[i - 1].Id && e.Target == listing.Id) &&
                definition.Edges.Count(e => e.Source == listing.Id && e.Target == ordered[i].Id) == 1;
            if (direct + (throughListing ? 1 : 0) != 1)
                return Error.Validation("Node connections must preserve both approval gates.");
        }
        try
        {
            if (requireProduct)
            {
                var input = WorkflowJson.Config<ProductInputConfig>(definition.Nodes.Single(x => x.Type == "product-input"));
                if (input.BatchId == Guid.Empty || input.ProductId == Guid.Empty) return Error.Validation("Select a batch and one product.");
            }
            else if (BatchIdOf(definition) == Guid.Empty) return Error.Validation("Select a batch in Product Input.");
            if (hasVideo)
            {
                var config = WorkflowJson.Config<GenerateVideoConfig>(definition.Nodes.Single(x => x.Type == "generate-video"));
                if (!registry.IsModeEnabled(config.Mode)) return new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation);
                // A batch run chooses each design's images itself, so its scenes may leave them open.
                var validation = requireProduct ? videoValidator.Validate(config) : videoValidator.ValidateForBatch(config);
                if (!validation.IsValid) return Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage)));
                if (ProductConfigsError(config) is { } productError) return productError;
                var assets = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
                if (assets.MockupIds is { Count: > 8 } || assets.MockupIds?.Distinct().Count() != assets.MockupIds?.Count)
                    return Error.Validation("Select at most eight distinct mockups.");
            }
            if (definition.Nodes.SingleOrDefault(x => x.Type == "publish-printify") is { } printifyNode)
            {
                // The Printify product is set on Apply mock-up, per product type; the product's own type is checked when
                // the step runs. A workflow saved before that keeps the product on the node itself.
                var printify = WorkflowJson.Config<PrintifyPublishConfig>(printifyNode);
                // Read from the approved mock-ups, the product is only known when the step runs.
                var printifyProblem = MockupPrintifyConfig.From(definition) is { HasProducts: true } || printify.FromMockups
                    ? printify.ProfitProblem()
                    : printify.Problem();
                if (printifyProblem is not null) return Error.Validation(printifyProblem);
            }
            if (definition.Nodes.SingleOrDefault(x => x.Type == "publish-etsy") is { } etsyNode
                && WorkflowJson.Config<EtsyPublishConfig>(etsyNode).Problem() is { } etsyProblem)
                return Error.Validation(etsyProblem);
        }
        catch (JsonException) { return Error.Validation("Invalid node configuration."); }
        return null;
    }

    /// <summary>Products of a batch with video settings of their own at most.</summary>
    public const int MaximumProductConfigs = 50;

    /// <summary>
    /// Each product's own settings are checked as the shared ones are, and have to make a video for each of its designs.
    /// </summary>
    private Error? ProductConfigsError(GenerateVideoConfig config)
    {
        if (config.ProductConfigs is not { Count: > 0 } products) return null;
        if (products.Count > MaximumProductConfigs)
            return Error.Validation($"At most {MaximumProductConfigs} products can have video settings of their own.");
        foreach (var own in products.Values)
        {
            if (own is null || own.ProductConfigs is { Count: > 0 } || !SupportsBatchVideos(own))
                return Error.Validation("A product's own video settings have to be a scene list, without the Product on Model pack.");
            if (!registry.IsModeEnabled(own.Mode)) return new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation);
            var validation = videoValidator.ValidateForBatch(own);
            if (!validation.IsValid)
                return Error.Validation("A product's own video settings: " + string.Join(" ", validation.Errors.Select(x => x.ErrorMessage)));
        }
        return null;
    }

    /// <summary>The batch chosen in Product Input, read without requiring a product, or <see cref="Guid.Empty"/>.</summary>
    public static Guid BatchIdOf(WorkflowDefinition definition) =>
        definition.Nodes.SingleOrDefault(x => x.Type == "product-input") is { } input &&
        input.Config.ValueKind == JsonValueKind.Object &&
        input.Config.TryGetProperty("batchId", out var value) && value.ValueKind == JsonValueKind.String &&
        Guid.TryParse(value.GetString(), out var batchId) ? batchId : Guid.Empty;

    /// <summary>
    /// Whether one scene list can make the video of every design. AI scenes are billed per clip, so the run confirms
    /// their total cost once for the whole group; the pack template is left out because it renders one product's
    /// shared mock-up and character.
    /// </summary>
    public static bool SupportsBatchVideos(GenerateVideoConfig config) =>
        config.Template != "product_on_model_pack" && config.Mode switch
        {
            "standard" => config.AiScenes is null || config.AiScenes.All(scene => scene.Mode == "standard"),
            // The legacy single-shot settings name one product's mock-up, so only a scene list maps to every design.
            "ai_shot" => config.AiScenes is { Count: > 0 },
            _ => false,
        };
}

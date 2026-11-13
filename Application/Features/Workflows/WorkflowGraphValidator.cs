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
            return Error.Validation("Connect the steps in one line: Product Input → Apply Mockup → Mockup Approval (→ Generate Video → Review Video), then the outputs. The design steps may come before Apply Mockup.");
        if (definition.Nodes.Select(x => x.Id).Distinct().Count() != definition.Nodes.Count || definition.Nodes.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 100))
            return Error.Validation("Node IDs must be unique and non-empty.");
        var enabled = registry.Get().Nodes.Where(x => x.Enabled).Select(x => x.Type).ToHashSet();
        if (definition.Nodes.Any(x => !enabled.Contains(x.Type)))
            return new("UnsupportedNode", "This workflow contains a node without a backend executor.", ErrorType.Validation);
        var hasVideo = definition.Nodes.Any(x => x.Type == "generate-video");
        // Every run ends in at least one output; they are peers, each at most once.
        var outputs = definition.Nodes.Where(x => WorkflowCapabilityRegistry.OutputSteps.Contains(x.Type)).ToArray();
        if (outputs.Length == 0)
            return Error.Validation("Add an output: Export ZIP, Push to Printify, Publish to Etsy or Push to media.");
        if (WorkflowCapabilityRegistry.OutputSteps.Any(type => outputs.Count(x => x.Type == type) > 1))
            return Error.Validation("Each output may appear only once.");
        if (hasVideo && WorkflowCapabilityRegistry.Pipeline.Any(type => definition.Nodes.Count(x => x.Type == type) != 1))
            return Error.Validation("A video run needs Product Input, Apply Mockup, Mockup Approval, Generate Video and Review Video, each once.");
        // Without a video the outputs follow Mockup Approval, so the seller still approves what goes out.
        if (!hasVideo)
        {
            if (definition.Nodes.Any(x => x.Type == "review-video"))
                return Error.Validation("Review Video needs Generate Video: add it, or remove Review Video.");
            if (outputs.Any(x => x.Type == "export-zip"))
                return Error.Validation("Export ZIP packages the approved video: add Generate Video and Review Video, or use another output.");
            if (WorkflowCapabilityRegistry.PublishPipeline.Any(type => definition.Nodes.Count(x => x.Type == type) != 1))
                return Error.Validation("The outputs need Product Input → Apply Mockup → Mockup Approval before them, so the mock-ups are approved first.");
        }
        if (definition.Nodes.Count(x => x.Type == "product-input") != 1 || definition.Nodes.Count(x => x.Type == "generate-listing") > 1)
            return Error.Validation("Product Input is required once and Listing Content may appear only once.");
        if (WorkflowCapabilityRegistry.BatchSteps.Any(type => definition.Nodes.Count(x => x.Type == type) > 1))
            return Error.Validation("Each design step may appear only once.");
        // The steps before the outputs form one line in order, so neither approval gate can be bypassed and the design
        // steps stay ahead of the mock-ups. Listing Content is written from the product's own details and waits for no
        // approval: it may hang off any step, or sit between two of them, without being one of the steps checked.
        var listing = definition.Nodes.SingleOrDefault(x => x.Type == "generate-listing");
        if (listing is not null && definition.Edges.Count(e => e.Target == listing.Id) != 1)
            return Error.Validation("Connect Listing Content after exactly one step.");
        var core = definition.Nodes.Where(x => x != listing && !outputs.Contains(x))
            .OrderBy(x => Array.IndexOf(WorkflowCapabilityRegistry.CoreOrder, x.Type)).ToArray();
        bool Connected(string from, string to) =>
            definition.Edges.Count(e => e.Source == from && e.Target == to)
            + (listing is not null && definition.Edges.Any(e => e.Source == from && e.Target == listing.Id)
               && definition.Edges.Count(e => e.Source == listing.Id && e.Target == to) == 1 ? 1 : 0) == 1;
        for (var i = 1; i < core.Length; i++)
        {
            if (!Connected(core[i - 1].Id, core[i].Id))
                return Error.Validation("Node connections must preserve both approval gates.");
        }
        // Each output takes one connection: from the last step (through Listing Content too) or from another output,
        // so they may sit side by side or one after another in any order, and all are reached from the last step.
        var last = core[^1].Id;
        var outputIds = outputs.Select(x => x.Id).ToHashSet();
        foreach (var output in outputs)
        {
            var from = definition.Edges.Where(e => e.Target == output.Id).ToArray();
            if (from.Length != 1 || !(from[0].Source == last || outputIds.Contains(from[0].Source) || from[0].Source == listing?.Id))
                return Error.Validation("Connect each output after the last step (Review Video, or Mockup Approval without a video) or after another output.");
        }
        if (definition.Edges.Any(e => outputIds.Contains(e.Source) && !outputIds.Contains(e.Target) && e.Target != listing?.Id))
            return Error.Validation("Nothing but another output may follow an output.");
        var reached = new HashSet<string> { last };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var edge in definition.Edges.Where(e => reached.Contains(e.Source) && (outputIds.Contains(e.Target) || e.Target == listing?.Id)))
                grew |= reached.Add(edge.Target);
        }
        if (!outputIds.All(reached.Contains))
            return Error.Validation("Connect each output after the last step (Review Video, or Mockup Approval without a video) or after another output.");
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

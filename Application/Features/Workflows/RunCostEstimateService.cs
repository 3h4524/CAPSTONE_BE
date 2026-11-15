using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>One line of what a Run is expected to cost.</summary>
/// <param name="Kind">"design_images", "ai_video" (one line per product) or "model_photos".</param>
/// <param name="Label">What the line is about: the step, or the product for an AI video.</param>
/// <param name="Detail">How the amount is made up, as the seller reads it.</param>
/// <param name="EstimatedCostUsd">Zero for work that is not billed (model photos are composited without AI).</param>
public sealed record RunCostItem(string Kind, string Label, string Detail, int Quantity, decimal EstimatedCostUsd);

/// <summary>What one Run of the saved workflow is expected to cost, before anything is made.</summary>
/// <param name="AiVideoBudgetUsd">
/// The part for AI video clips. Confirming the estimate allows up to this much: a batch video run whose Mockup Approval
/// is automatic starts its videos without asking again when their exact quote stays within it.
/// </param>
public sealed record RunCostEstimateResponse(IReadOnlyList<RunCostItem> Items, decimal TotalUsd, decimal AiVideoBudgetUsd, string Note);

public interface IRunCostEstimateService
{
    Task<Result<RunCostEstimateResponse>> EstimateAsync(Guid workflowId, CancellationToken ct);
}

/// <summary>
/// Estimates a Run before it starts, from the saved workflow and the products of the batch in Product Input the Run
/// goes through (its pending ones when it makes designs). The designs do not exist yet, so the counts are upper
/// bounds: every product without a design gets every variant, and every design gets a video.
/// </summary>
public sealed class RunCostEstimateService(ICurrentUser user, IRepository<Workflow> workflows, IRepository<Product> products,
    IRepository<DesignImage> designs, IEnumerable<IImageGenerationProvider> imageProviders,
    IRepository<MockupImage>? mockups = null) : IRunCostEstimateService
{
    private sealed record DesignImageConfig(string ImageProvider = ImageGenerationProviders.Default, int Variants = 2);

    public async Task<Result<RunCostEstimateResponse>> EstimateAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await workflows.GetByIdAsync(workflowId, ct);
        if (workflow is null || workflow.UserId != user.UserId || workflow.DeletedAt != null)
            return Result.Failure<RunCostEstimateResponse>(Error.NotFound("WorkflowNotFound", "Workflow not found."));
        var definition = WorkflowJson.Read<WorkflowDefinition>(workflow.Definition);
        var batchId = WorkflowGraphValidator.BatchIdOf(definition);
        var all = batchId == Guid.Empty ? Array.Empty<Product>()
            : (await products.FindAsync(x => x.BatchId == batchId && x.UserId == workflow.UserId && x.DeletedAt == null, ct))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id).ToArray();
        // Only what this Run makes is priced. A workflow with design steps makes its designs as a job of the batch's pending
        // products (BatchService.ApproveAsync), and its videos and listings are for that job's products only; a product
        // finished by an earlier Run is not made again. With nothing pending, or without design steps, the Run goes on
        // with every product of the batch.
        var pending = all.Where(x => x.ProcessingStatus == BatchJobProductStatuses.Pending).ToArray();
        var batch = definition.Nodes.Any(x => WorkflowCapabilityRegistry.BatchSteps.Contains(x.Type)) && pending.Length > 0 ? pending : all;
        var ids = batch.Select(x => x.Id).ToArray();
        IReadOnlyList<DesignImage> existing = ids.Length == 0 ? Array.Empty<DesignImage>()
            : await designs.FindAsync(x => ids.Contains(x.ProductId) && x.DeletedAt == null && x.ApprovalStatus != "rejected", ct);
        var designsOf = existing.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.ToArray());

        var items = new List<RunCostItem>();
        var designNode = definition.Nodes.SingleOrDefault(x => x.Type == "design-image");
        var variants = 0;
        if (designNode is not null)
        {
            var design = WorkflowJson.Config<DesignImageConfig>(designNode);
            variants = Math.Clamp(design.Variants, 1, 4);
            var provider = imageProviders.FirstOrDefault(x => string.Equals(x.Provider, design.ImageProvider, StringComparison.OrdinalIgnoreCase))
                ?? imageProviders.FirstOrDefault(x => x.Provider == ImageGenerationProviders.Default);
            var toDesign = batch.Count(product => !designsOf.ContainsKey(product.Id));
            if (toDesign > 0 && provider is not null)
                items.Add(new("design_images", "Design images", $"{toDesign} product{Plural(toDesign)} × {variants} variant{Plural(variants)}",
                    toDesign * variants, toDesign * variants * provider.EstimatedCostPerImageUsd));
        }

        if (definition.Nodes.SingleOrDefault(x => x.Type == "generate-video") is { } videoNode)
        {
            var shared = WorkflowJson.Config<GenerateVideoConfig>(videoNode);
            var withPerson = 0;
            // A design made before this Run that already has its model photo gets no second one, so it is not billed again.
            var photographed = mockups is null || ids.Length == 0 ? new HashSet<string>()
                : (await mockups.FindAsync(x => ids.Contains(x.ProductId) && x.Role == ModelPhotoMockupService.Role, ct))
                    .GroupBy(x => x.ArtworkGroupKey ?? x.ProductId.ToString())
                    .Where(group => PersonModelPhotos.Has(group))
                    .Select(group => group.Key)
                    .ToHashSet(StringComparer.Ordinal);
            foreach (var product in batch)
            {
                // A product given settings of its own before the run renders every design with them.
                var config = shared.ForProduct(product.Id);
                var (clips, seconds, perVideo) = AiCostPerVideo(config);
                // A product designed in this run gets every variant; one designed before keeps its approved design,
                // else every design not rejected.
                var videos = designsOf.TryGetValue(product.Id, out var own)
                    ? own.Any(x => x.ApprovalStatus == "approved") ? 1 : own.Length
                    : Math.Max(variants, 1);
                if (clips > 0)
                    items.Add(new("ai_video", product.Name, $"{videos} video{Plural(videos)} × {clips} AI clip{Plural(clips)} ({seconds:0.#} s)",
                        videos * clips, videos * perVideo));
                if (PersonModelPhotos.Needs(config))
                    withPerson += own is null ? videos
                        : (own.Any(x => x.ApprovalStatus == "approved") ? own.Where(x => x.ApprovalStatus == "approved") : own)
                            .Count(design => !photographed.Contains(design.Id.ToString()));
            }
            var ideogram = imageProviders.FirstOrDefault(x => x.Provider == ImageGenerationProviders.Ideogram);
            if (withPerson > 0 && ideogram is not null)
                items.Add(new("model_photos", "Model photos", "One per design for its scenes with a person: Ideogram puts the design on a system model",
                    withPerson, withPerson * ideogram.EstimatedCostPerImageUsd));
        }

        var aiBudget = items.Where(x => x.Kind == "ai_video").Sum(x => x.EstimatedCostUsd);
        return Result.Success(new RunCostEstimateResponse(items, items.Sum(x => x.EstimatedCostUsd), aiBudget,
            "Estimate only, as an upper bound: the designs are not made yet. Providers bill successful generations."));
    }

    /// <summary>The AI clips one video makes: how many, how long together, and their cost.</summary>
    public static (int Clips, decimal Seconds, decimal CostUsd) AiCostPerVideo(GenerateVideoConfig config)
    {
        if (config.AiScenes is { Count: > 0 } scenes)
        {
            var ai = scenes.Where(scene => scene.Mode != "standard").ToArray();
            return (ai.Length, ai.Sum(scene => (decimal)scene.DurationSeconds),
                ai.Sum(scene => scene.DurationSeconds * PricePerSecond(scene.AiVideoModelKey ?? config.AiVideoModelKey)));
        }
        return config.Mode == "ai_shot"
            ? (1, config.DurationSeconds, config.DurationSeconds * PricePerSecond(config.AiVideoModelKey))
            : (0, 0m, 0m);
    }

    private static decimal PricePerSecond(string? modelKey) =>
        modelKey == AiVideoModel.LiteModelKey ? AiVideoModel.LitePricePerSecondUsd : AiVideoModel.PricePerSecondUsd;

    private static string Plural(int count) => count == 1 ? "" : "s";
}

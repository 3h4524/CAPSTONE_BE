using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

/// <summary>Runs the publish steps of workflow runs in the background, as the run's owner.</summary>
public interface IWorkflowPublishService
{
    /// <summary>Whose run the step belongs to: the background worker acts as that seller.</summary>
    Task<Guid?> OwnerOfAsync(Guid nodeRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the step one stage further. Returns how long to wait before calling again (Printify is still publishing),
    /// or null once the step succeeded, failed or no longer needs work.
    /// </summary>
    Task<TimeSpan?> ProcessAsync(Guid nodeRunId, CancellationToken cancellationToken);

    /// <summary>Publish steps left running, for a restarted process to pick up again.</summary>
    Task<IReadOnlyList<Guid>> UnfinishedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Push to Printify creates the product, prices each variant for the node's target profit and asks Printify to
/// publish it to Etsy, then follows it until the Etsy listing exists and adds the APCS media to it. Publish to Etsy
/// creates one draft listing. External calls happen outside any transaction; the result is written under the run's
/// lock, and the run then advances.
/// </summary>
public sealed class WorkflowPublishService(
    IRepository<WorkflowNodeRun> nodes,
    IRepository<WorkflowRun> runs,
    IWorkflowStateRepository state,
    IUnitOfWork unitOfWork,
    WorkflowRuntime runtime,
    IPrintifyService printify,
    IEtsyPublishingService etsy,
    TimeProvider time,
    ILogger<WorkflowPublishService> logger) : IWorkflowPublishService
{
    /// <summary>How often a product Printify is publishing is checked.</summary>
    internal static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(20);

    /// <summary>After this long without an Etsy listing the step fails, for the seller to look on Printify.</summary>
    internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);

    /// <summary>APCS mock-ups added to the listing Printify creates, in front of its own.</summary>
    internal const int AttachedMockups = 5;

    public async Task<Guid?> OwnerOfAsync(Guid nodeRunId, CancellationToken cancellationToken) =>
        await nodes.Query().Where(node => node.Id == nodeRunId).Select(node => (Guid?)node.WorkflowRun.UserId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> UnfinishedAsync(CancellationToken cancellationToken) =>
        await nodes.Query()
            .Where(node => (node.NodeType == "publish-printify" || node.NodeType == "publish-etsy") && node.Status == "running"
                && node.WorkflowRun.Status == "running")
            .Select(node => node.Id)
            .ToListAsync(cancellationToken);

    public async Task<TimeSpan?> ProcessAsync(Guid nodeRunId, CancellationToken cancellationToken)
    {
        var snapshot = await nodes.Query().Where(node => node.Id == nodeRunId)
            .Select(node => new { node.NodeType, node.Status, node.InputSnapshot, node.OutputSnapshot, node.WorkflowRunId, node.Attempt, RunStatus = node.WorkflowRun.Status, node.WorkflowRun.ProductId })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is not { Status: "running", RunStatus: "running" } || !WorkflowCapabilityRegistry.PublishSteps.Contains(snapshot.NodeType))
            return null;

        var current = ReadState(snapshot.OutputSnapshot);
        Outcome outcome;
        try
        {
            outcome = snapshot.NodeType == "publish-printify"
                ? await PrintifyAsync(snapshot.ProductId, snapshot.InputSnapshot, current, cancellationToken)
                : await EtsyAsync(snapshot.ProductId, snapshot.InputSnapshot, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Publish step {NodeRunId} stopped unexpectedly.", nodeRunId);
            outcome = Outcome.Fail("Publishing stopped unexpectedly. Retry this step.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var run = await state.LockRunAsync(snapshot.WorkflowRunId, cancellationToken);
        var node = await nodes.GetByIdAsync(nodeRunId, cancellationToken);
        // The run may have been cancelled, or the step retried, while Printify or Etsy was answering.
        var fresh = await nodes.Query().Where(row => row.Id == nodeRunId).Select(row => new { row.Status, row.Attempt, RunStatus = row.WorkflowRun.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null || node is null || fresh is not { Status: "running", RunStatus: "running" } || fresh.Attempt != snapshot.Attempt)
            return null;

        var now = time.GetUtcNow().UtcDateTime;
        node.UpdatedAt = now;
        switch (outcome.Kind)
        {
            case OutcomeKind.Done:
                node.OutputSnapshot = WorkflowJson.Write(outcome.State);
                node.Status = "succeeded"; node.Stage = "Published"; node.Progress = 100; node.ErrorMessage = null;
                run.Revision++; run.UpdatedAt = now;
                await runtime.AdvanceAsync(run, cancellationToken);
                break;
            case OutcomeKind.Failed:
                await runtime.FailAsync(run, node.Id, outcome.Message!, cancellationToken);
                run.UpdatedAt = now;
                break;
            default:
                node.OutputSnapshot = WorkflowJson.Write(outcome.State);
                node.Stage = "Publishing on Etsy"; node.Progress = 60;
                run.Revision++; run.UpdatedAt = now;
                break;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome.Kind == OutcomeKind.Pending ? PollEvery : null;
    }

    private async Task<Outcome> PrintifyAsync(Guid productId, string input, PublishNodeState current, CancellationToken cancellationToken)
    {
        var config = WorkflowJson.Read<PrintifyPublishConfig>(input);
        if (config.Problem() is { } problem)
            return Outcome.Fail(problem);
        if (current is { Stage: "publishing", PrintifyProductId: { } publishingId })
            return await FollowAsync(productId, publishingId, current, cancellationToken);

        var draft = await printify.GetDraftAsync(productId, cancellationToken);
        if (draft.IsFailure)
            return Outcome.Fail(draft.Error.Message);
        var design = config.DesignId is { } designId ? draft.Value.Designs.FirstOrDefault(row => row.Id == designId) : draft.Value.Designs.FirstOrDefault();
        if (design is null)
            return Outcome.Fail(config.DesignId is null
                ? "Push to Printify needs an approved design of this product."
                : "The design chosen on Push to Printify is no longer approved; choose another one.");

        var catalog = await printify.GetCatalogAsync(config.BlueprintId!.Value, config.PrintProviderId!.Value, cancellationToken);
        if (catalog.IsFailure)
            return Outcome.Fail(catalog.Error.Message);
        var colors = config.Colors!.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sizes = config.Sizes!.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variantIds = catalog.Value.Variants
            .Where(variant => (variant.Color is null || colors.Contains(variant.Color)) && (variant.Size is null || sizes.Contains(variant.Size)))
            .Select(variant => variant.Id)
            .ToArray();
        if (variantIds.Length == 0)
            return Outcome.Fail("This provider no longer makes any of the chosen colors and sizes; choose them again on Push to Printify.");
        if (variantIds.Length > Shops.Validators.CreatePrintifyProductValidator.MaxVariants)
            return Outcome.Fail($"The chosen colors and sizes make {variantIds.Length} variants; Etsy allows {Shops.Validators.CreatePrintifyProductValidator.MaxVariants}.");

        var created = await printify.CreateProductAsync(productId, new CreatePrintifyProductRequestDto(
            design.Id, config.BlueprintId.Value, config.BlueprintTitle, config.PrintProviderId.Value, config.PrintProviderTitle,
            variantIds, draft.Value.Title, draft.Value.Description, draft.Value.Tags, PrintifyService.DefaultPrice), cancellationToken);
        if (created.IsFailure)
            return Outcome.Fail(created.Error.Message);
        var product = created.Value;
        var warnings = product.Warnings.ToList();
        if (config.PublishToEtsy == false)
            return Outcome.Done(new PublishNodeState("done", product.PrintifyProductId, Warnings: warnings));

        // Each variant is priced against what Printify charges for it, so larger sizes cost the buyer more.
        var profit = config.TargetProfit ?? PrintifyPublishConfig.DefaultProfit;
        var shipping = product.UsShippingFirstItem ?? 0;
        var prices = product.Variants
            .Select(variant => new PrintifyVariantPriceDto(variant.Id, PrintifyPricing.PriceForProfit(variant.Cost, shipping, profit)))
            .ToArray();
        var published = await printify.PublishAsync(productId, product.PrintifyProductId, new PublishPrintifyProductRequestDto(prices), cancellationToken);
        if (published.IsFailure)
            return Outcome.Fail(published.Error.Message);

        var media = config.AddMockups == false ? [] : draft.Value.Mockups.Take(AttachedMockups).Select(mockup => mockup.Id).ToArray();
        var next = new PublishNodeState("publishing", product.PrintifyProductId, time.GetUtcNow().UtcDateTime,
            Warnings: warnings, MockupIds: media, IncludeVideo: config.AddMockups != false && draft.Value.Video is not null);
        return await FollowAsync(productId, product.PrintifyProductId, next, cancellationToken);
    }

    /// <summary>Asks Printify how publishing goes; once on Etsy, the APCS media are added (once) and the step is done.</summary>
    private async Task<Outcome> FollowAsync(Guid productId, string printifyProductId, PublishNodeState current, CancellationToken cancellationToken)
    {
        var synced = await printify.SyncAsync(productId, printifyProductId,
            new SyncPrintifyProductRequestDto(current.MockupIds ?? [], current.IncludeVideo), cancellationToken);
        if (synced.IsFailure)
            return Outcome.Fail(synced.Error.Message);
        var product = synced.Value;
        var warnings = (current.Warnings ?? []).Concat(product.Warnings).Distinct().ToArray();
        if (product.State == PrintifyStages.Published)
            return Outcome.Done(current with
            {
                Stage = "done", EtsyListingId = product.EtsyListingId, EtsyUrl = product.EtsyUrl, MediaAttached = product.MediaAttached, Warnings = warnings,
            });
        if (product.State == PrintifyStages.Created)
            return Outcome.Fail(product.Warnings.FirstOrDefault() ?? "Printify finished without creating the Etsy listing. Check the product on Printify, then retry this step.");
        if (current.PublishRequestedAt is { } asked && time.GetUtcNow().UtcDateTime - asked > GiveUpAfter)
            return Outcome.Fail("Printify has not finished publishing to Etsy after 15 minutes. Check the product on Printify, then retry this step.");
        return Outcome.Pending(current with { Warnings = warnings });
    }

    private async Task<Outcome> EtsyAsync(Guid productId, string input, CancellationToken cancellationToken)
    {
        var config = WorkflowJson.Read<EtsyPublishConfig>(input);
        if (config.Problem() is { } problem)
            return Outcome.Fail(problem);
        var draft = await etsy.GetDraftAsync(productId, cancellationToken);
        if (draft.IsFailure)
            return Outcome.Fail(draft.Error.Message);
        var content = draft.Value;
        if (content.ConnectionStatus != ShopConnectionStatuses.Connected)
            return Outcome.Fail(content.ConnectionStatus == ShopConnectionStatuses.Unavailable
                ? "Etsy is not answering right now. Retry this step in a moment."
                : "Connect your Etsy shop on the Shop page, then retry this step.");
        var mockupIds = content.Mockups.Take(10).Select(mockup => mockup.Id).ToArray();
        if (mockupIds.Length == 0)
            return Outcome.Fail("Publish to Etsy needs at least one approved mock-up for the listing photos.");
        var designIds = config.Type == "download" ? content.Designs.Take(Shops.Validators.PublishEtsyDraftValidator.MaxFiles).Select(design => design.Id).ToArray() : [];
        if (config.Type == "download" && designIds.Length == 0)
            return Outcome.Fail("A digital listing needs an approved design for buyers to download.");

        var result = await etsy.PublishDraftAsync(productId, new PublishEtsyDraftRequestDto(
            content.Title, content.Description, content.Tags, config.Price!.Value, config.Quantity ?? 999, config.TaxonomyId!.Value, config.TaxonomyPath,
            config.Type, config.WhoMade ?? "i_did", config.WhenMade ?? "made_to_order",
            config.Type == "physical" ? config.ShippingProfileId : null, null, [], mockupIds,
            config.IncludeVideo != false && content.Video is not null, designIds), cancellationToken);
        if (result.IsFailure)
            return Outcome.Fail(result.Error.Message);
        var listing = result.Value;
        return Outcome.Done(new PublishNodeState("done", EtsyListingId: listing.ListingId, EtsyUrl: listing.EditUrl,
            MediaAttached: listing.ImagesUploaded + (listing.VideoUploaded ? 1 : 0) + listing.FilesUploaded, Warnings: listing.Warnings));
    }

    private static PublishNodeState ReadState(string json)
    {
        try
        {
            return WorkflowJson.Read<PublishNodeState>(json);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return new PublishNodeState("queued");
        }
    }

    private enum OutcomeKind { Done, Pending, Failed }

    private sealed record Outcome(OutcomeKind Kind, PublishNodeState? State, string? Message)
    {
        public static Outcome Done(PublishNodeState state) => new(OutcomeKind.Done, state, null);
        public static Outcome Pending(PublishNodeState state) => new(OutcomeKind.Pending, state, null);
        public static Outcome Fail(string message) => new(OutcomeKind.Failed, null, message);
    }
}

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
    ILogger<WorkflowPublishService> logger,
    IListingPace? pace = null,
    IPrintifyMockupMatcher? matcher = null,
    IWorkflowRunNotifier? runNotifier = null) : IWorkflowPublishService
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
            .Where(node => node.Status == "running"
                && (node.NodeType == WorkflowCapabilityRegistry.PrintifyStep
                    ? node.WorkflowRun.Status != "cancelled" && node.WorkflowRun.Status != "completed"
                    : node.NodeType == "publish-etsy" && node.WorkflowRun.Status == "running"))
            .Select(node => node.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Whether a step of a run in this state is still worked on. Push to Printify runs beside the video, so it goes on
    /// while the video waits for review or has failed; Publish to Etsy follows the video and needs the run running.
    /// </summary>
    private static bool Works(string nodeType, string runStatus) => nodeType == WorkflowCapabilityRegistry.PrintifyStep
        ? runStatus is not ("cancelled" or "completed")
        : runStatus == "running";

    public async Task<TimeSpan?> ProcessAsync(Guid nodeRunId, CancellationToken cancellationToken)
    {
        var snapshot = await nodes.Query().Where(node => node.Id == nodeRunId)
            .Select(node => new { node.NodeType, node.Status, node.InputSnapshot, node.OutputSnapshot, node.WorkflowRunId, node.Attempt, RunStatus = node.WorkflowRun.Status, node.WorkflowRun.ProductId, node.WorkflowRun.DefinitionSnapshot, node.WorkflowRun.Product.ProductType, node.WorkflowRun.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is not { Status: "running" } || !WorkflowCapabilityRegistry.PublishSteps.Contains(snapshot.NodeType)
            || !Works(snapshot.NodeType, snapshot.RunStatus))
            return null;

        var current = ReadState(snapshot.OutputSnapshot);
        Outcome outcome;
        try
        {
            // A new listing waits its turn instead of failing: the step carries on by itself once the shop may list again.
            // A product only made on Printify adds no Etsy listing, so it never waits for the shop's pace.
            if (current.Stage != "publishing" && pace is not null && AddsEtsyListing(snapshot.NodeType, snapshot.InputSnapshot)
                && await pace.CheckAsync(snapshot.UserId, cancellationToken) is { Allowed: false, RetryAtUtc: { } retryAt })
                outcome = Outcome.Wait(current, retryAt);
            else
            outcome = snapshot.NodeType == "publish-printify"
                ? await PrintifyAsync(snapshot.ProductId, snapshot.InputSnapshot, Source(snapshot.DefinitionSnapshot), snapshot.ProductType, current, cancellationToken)
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
        // Stopped while Printify or Etsy was already making it: the product exists, so the step says so (the run stays stopped).
        var madeAnyway = outcome.Kind == OutcomeKind.Done && fresh is { Status: "cancelled" };
        if (run is null || node is null || fresh is null || fresh.Attempt != snapshot.Attempt
            || (!madeAnyway && (fresh.Status != "running" || !Works(snapshot.NodeType, fresh.RunStatus))))
            return null;

        var now = time.GetUtcNow().UtcDateTime;
        node.UpdatedAt = now;
        switch (outcome.Kind)
        {
            case OutcomeKind.Done:
                node.OutputSnapshot = WorkflowJson.Write(outcome.State);
                node.Status = "succeeded"; node.Stage = "Published"; node.Progress = 100; node.ErrorMessage = null;
                run.Revision++; run.UpdatedAt = now;
                // Push to Printify ends its own branch: the run goes on only if it was waiting for it to finish.
                if (run.Status == "running") await runtime.AdvanceAsync(run, cancellationToken);
                break;
            case OutcomeKind.Failed when snapshot.NodeType == WorkflowCapabilityRegistry.PrintifyStep:
                // Printify failing does not stop the video: only its step fails, and the run reports it once the video
                // part is done too (WorkflowRuntime.AdvanceAsync).
                node.Status = "failed"; node.Stage = null; node.Progress = 0; node.ErrorMessage = outcome.Message;
                run.Revision++; run.UpdatedAt = now;
                if (run.Status == "running") await runtime.AdvanceAsync(run, cancellationToken);
                break;
            case OutcomeKind.Failed:
                await runtime.FailAsync(run, node.Id, outcome.Message!, cancellationToken);
                run.UpdatedAt = now;
                break;
            case OutcomeKind.Waiting:
                node.Stage = $"Waiting until {outcome.RetryAtUtc:HH:mm} UTC: the shop's listing pace"; node.Progress = 0;
                run.Revision++; run.UpdatedAt = now;
                break;
            default:
                node.OutputSnapshot = WorkflowJson.Write(outcome.State);
                node.Stage = "Publishing on Etsy"; node.Progress = 60;
                run.Revision++; run.UpdatedAt = now;
                break;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // The pages following the run, or its batch, read it again: the step ran in the background, beside the video.
        if (runNotifier is not null) await runNotifier.RunChangedAsync(run, cancellationToken);
        return outcome.Kind switch
        {
            OutcomeKind.Pending => PollEvery,
            OutcomeKind.Waiting => outcome.RetryAtUtc!.Value - now > PollEvery ? outcome.RetryAtUtc.Value - now : PollEvery,
            _ => null,
        };
    }

    /// <summary>Whether the step adds a listing to the seller's Etsy shop: Publish to Etsy, or Push to Printify publishing to Etsy.</summary>
    internal static bool AddsEtsyListing(string nodeType, string input)
    {
        if (nodeType != WorkflowCapabilityRegistry.PrintifyStep) return true;
        try { return WorkflowJson.Read<PrintifyPublishConfig>(input).PublishToEtsy != false; }
        catch (System.Text.Json.JsonException) { return true; }
    }

    /// <summary>The Printify products set on Apply mock-up, or null when the definition cannot be read.</summary>
    private static MockupPrintifyConfig? Source(string definition)
    {
        try { return MockupPrintifyConfig.From(WorkflowJson.Read<WorkflowDefinition>(definition)); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private async Task<Outcome> PrintifyAsync(Guid productId, string input, MockupPrintifyConfig? source, string productType,
        PublishNodeState current, CancellationToken cancellationToken)
    {
        // Already created and publishing: only follow it, without reading the catalog again.
        if (current is { Stage: "publishing", PrintifyProductId: { } publishingId })
            return await FollowAsync(productId, publishingId, current, cancellationToken);
        var config = WorkflowJson.Read<PrintifyPublishConfig>(input);
        var fromMockup = false;
        string? madeAs = null;
        IReadOnlyList<string> matchWarnings = [];
        if (source is { HasProducts: true })
        {
            // The mock-ups show this garment, so Printify makes exactly it: the listing and the item shipped match.
            if (source.SetupFor(productType) is not { } setup)
                return Outcome.Fail($"Apply mock-up has no Printify product for {productType}; choose a linked template there, then retry this step.");
            config = config.WithSetup(setup);
            fromMockup = true;
        }
        else if (config.FromMockups && matcher is not null)
        {
            // System templates are named after the Printify product and their colors are mapped to Printify's names,
            // so the approved mock-ups say what Printify makes; the seller's own templates leave it to this node.
            var match = await matcher.MatchAsync(productId, config, cancellationToken);
            if (match.IsFailure)
                return Outcome.Fail($"Push to Printify: {match.Error.Message}");
            if (match.Value is { } matched)
            {
                config = config.WithSetup(matched.Setup);
                fromMockup = true;
                madeAs = matched.Summary;
                matchWarnings = matched.Warnings;
            }
            else if (config.BlueprintId is not > 0)
                return Outcome.Fail("Push to Printify: the approved mock-ups use your own templates, so the Printify product cannot be read from them. "
                    + "Choose the product, provider, colors and sizes on Push to Printify, then retry this step.");
        }
        if (config.Problem() is { } problem)
            return Outcome.Fail(problem);

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

        // Set on Apply mock-up, the print follows the mock-ups: where and how large the design sits on the garment's front,
        // and on each other side (back, sleeves) the approved mock-ups show it on.
        var placementMockup = fromMockup ? draft.Value.Mockups.FirstOrDefault() : null;
        var created = await printify.CreateProductAsync(productId, new CreatePrintifyProductRequestDto(
            design.Id, config.BlueprintId.Value, config.BlueprintTitle, config.PrintProviderId.Value, config.PrintProviderTitle,
            variantIds, draft.Value.Title, draft.Value.Description, draft.Value.Tags, PrintifyService.DefaultPrice, Position: config.Position,
            PlacementMockupId: placementMockup?.Id, PrintMockupSides: fromMockup), cancellationToken);
        if (created.IsFailure)
            return Outcome.Fail(created.Error.Message);
        var product = created.Value;
        var warnings = matchWarnings.Concat(product.Warnings).ToList();
        if (config.PublishToEtsy == false)
            return Outcome.Done(new PublishNodeState("done", product.PrintifyProductId, Warnings: warnings, MadeAs: madeAs));

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
        var leadPhoto = config.LeadPhoto ?? (fromMockup ? "printify" : "apcs");
        var next = new PublishNodeState("publishing", product.PrintifyProductId, time.GetUtcNow().UtcDateTime,
            Warnings: warnings, MockupIds: media,
            MockupsAfterPrintify: string.Equals(leadPhoto, "printify", StringComparison.OrdinalIgnoreCase), MadeAs: madeAs);
        return await FollowAsync(productId, product.PrintifyProductId, next, cancellationToken);
    }

    /// <summary>Asks Printify how publishing goes; once on Etsy, the APCS media are added (once) and the step is done.</summary>
    private async Task<Outcome> FollowAsync(Guid productId, string printifyProductId, PublishNodeState current, CancellationToken cancellationToken)
    {
        var synced = await printify.SyncAsync(productId, printifyProductId,
            new SyncPrintifyProductRequestDto(current.MockupIds ?? [], current.MockupsAfterPrintify), cancellationToken);
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
        // A physical listing opens on a photo that shows the item as it is; photos made with AI follow it.
        var photos = config.Type == "physical" && content.Mockups.FirstOrDefault(mockup => !mockup.AiMade) is { } real
            ? content.Mockups.Where(mockup => mockup.Id != real.Id).Prepend(real)
            : content.Mockups;
        var mockupIds = photos.Take(10).Select(mockup => mockup.Id).ToArray();
        if (mockupIds.Length == 0)
            return Outcome.Fail("Publish to Etsy needs at least one approved mock-up for the listing photos.");
        if (config.Type == "physical" && content.Mockups.All(mockup => mockup.AiMade))
            return Outcome.Fail($"Publish to Etsy: {ListingPhotos.NoRealPhoto}");
        var designIds = config.Type == "download" ? content.Designs.Take(Shops.Validators.PublishEtsyDraftValidator.MaxFiles).Select(design => design.Id).ToArray() : [];
        if (config.Type == "download" && designIds.Length == 0)
            return Outcome.Fail("A digital listing needs an approved design for buyers to download.");

        var result = await etsy.PublishDraftAsync(productId, new PublishEtsyDraftRequestDto(
            content.Title, content.Description, content.Tags, config.Price!.Value, config.Quantity ?? 999, config.TaxonomyId!.Value, config.TaxonomyPath,
            config.Type, config.WhoMade ?? "i_did", config.WhenMade ?? "made_to_order",
            config.Type == "physical" ? config.ShippingProfileId : null, null, config.Partners, mockupIds,
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

    private enum OutcomeKind { Done, Pending, Failed, Waiting }

    private sealed record Outcome(OutcomeKind Kind, PublishNodeState? State, string? Message, DateTime? RetryAtUtc = null)
    {
        public static Outcome Done(PublishNodeState state) => new(OutcomeKind.Done, state, null);
        public static Outcome Pending(PublishNodeState state) => new(OutcomeKind.Pending, state, null);
        public static Outcome Fail(string message) => new(OutcomeKind.Failed, null, message);
        /// <summary>Nothing was sent: the shop reached its listing pace, so the step tries again at <paramref name="retryAt"/>.</summary>
        public static Outcome Wait(PublishNodeState state, DateTime retryAt) => new(OutcomeKind.Waiting, state, null, retryAt);
    }
}

using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowRunService
{
    Task<Result<RunResponse>> StartAsync(Guid workflowId, StartRunRequest request, CancellationToken ct);
    Task<Result<RunResponse>> GetLatestAsync(Guid workflowId, CancellationToken ct);
    Task<Result<IReadOnlyList<VideoRunSummaryResponse>>> ListVideoRunsAsync(Guid workflowId, CancellationToken ct);
    Task<Result<RunResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<RunResponse>> ActionAsync(Guid id, RunActionRequest request, CancellationToken ct);
}

public sealed class WorkflowRunService(ICurrentUser user, IRepository<Workflow> workflows, IRepository<WorkflowRun> runs,
    IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<PromoVideo> videos, IRepository<MediaJob> jobs, IWorkflowStateRepository state, IUnitOfWork uow,
    WorkflowGraphValidator graph, WorkflowRuntime runtime, GenerateVideoConfigValidator validator,
    WorkflowCapabilityRegistry capabilities, IAiShotQuoteService quotes, TimeProvider time) : IWorkflowRunService
{
    public async Task<Result<RunResponse>> StartAsync(Guid workflowId, StartRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return Result.Failure<RunResponse>(Error.Validation("Provide an idempotency key of 1–100 characters."));
        await using var tx = await uow.BeginTransactionAsync(ct);
        await state.LockOwnerAsync(user.UserId!.Value, ct);
        var requestHash = WorkflowJson.Hash(WorkflowJson.Write(new { workflowId, request.ExpectedWorkflowRevision }));
        var existing = (await runs.FindAsync(x => x.UserId == user.UserId && x.IdempotencyKey == request.IdempotencyKey, ct)).SingleOrDefault();
        if (existing != null)
            return existing.RequestHash == requestHash ? Result.Success(await MapAsync(existing, ct)) : Result.Failure<RunResponse>(Error.Conflict("IdempotencyConflict", "Key was used for another request."));
        var workflow = await workflows.GetByIdAsync(workflowId, ct);
        if (workflow is null || workflow.UserId != user.UserId || workflow.DeletedAt != null) return Result.Failure<RunResponse>(Missing());
        if (workflow.Revision != request.ExpectedWorkflowRevision) return Result.Failure<RunResponse>(Stale());
        var definition = WorkflowJson.Read<WorkflowDefinition>(workflow.Definition);
        var error = graph.Validate(definition);
        if (error != null) return Result.Failure<RunResponse>(error);
        var input = WorkflowJson.Config<ProductInputConfig>(definition.Nodes.Single(x => x.Type == "product-input"));
        var product = await products.GetByIdAsync(input.ProductId, ct);
        if (product is null || product.UserId != user.UserId || product.DeletedAt != null || product.BatchId != input.BatchId)
            return Result.Failure<RunResponse>(Error.NotFound("ProductNotFound", "Product not found in the selected batch."));
        var now = time.GetUtcNow().UtcDateTime;
        var run = new WorkflowRun { Id = Guid.NewGuid(), WorkflowId = workflowId, UserId = product.UserId, ProductId = product.Id,
            WorkflowRevision = workflow.Revision, DefinitionSnapshot = workflow.Definition, Status = "running", Revision = 1,
            IdempotencyKey = request.IdempotencyKey, RequestHash = requestHash, CreatedAt = now, UpdatedAt = now };
        await runs.AddAsync(run, cancellationToken: ct);
        foreach (var node in definition.Nodes)
            await nodes.AddAsync(new() { Id = Guid.NewGuid(), WorkflowRunId = run.Id, NodeId = node.Id, NodeType = node.Type,
                Status = "pending", Attempt = 1, InputSnapshot = node.Config.GetRawText(), OutputSnapshot = "{}", CreatedAt = now, UpdatedAt = now }, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);
        await runtime.AdvanceAsync(run, ct); await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result.Success(await MapAsync(run, ct));
    }
    public async Task<Result<RunResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var run = await state.LockRunAsync(id, ct);
        if (run is null || run.UserId != user.UserId) return Result.Failure<RunResponse>(Missing());
        if (run.Status == "running")
        {
            var failed = (await jobs.FindAsync(x => x.WorkflowRunId == id && x.Status == "failed", ct)).FirstOrDefault();
            if (failed != null) await runtime.FailAsync(run, failed.WorkflowNodeRunId, failed.ErrorMessage ?? "Render failed.", ct);
        }
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success(await MapAsync(run, ct));
    }
    public async Task<Result<RunResponse>> GetLatestAsync(Guid workflowId, CancellationToken ct)
    {
        var latest = (await runs.FindAsync(x => x.WorkflowId == workflowId && x.UserId == user.UserId, ct))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        return latest is null
            ? Result.Failure<RunResponse>(Missing())
            : Result.Success(await MapAsync(latest, ct));
    }
    public async Task<Result<IReadOnlyList<VideoRunSummaryResponse>>> ListVideoRunsAsync(Guid workflowId, CancellationToken ct)
    {
        var ownedRuns = (await runs.FindAsync(x => x.WorkflowId == workflowId && x.UserId == user.UserId, ct))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToArray();
        if (ownedRuns.Length == 0) return Result.Success<IReadOnlyList<VideoRunSummaryResponse>>([]);

        var runIds = ownedRuns.Select(x => x.Id).ToArray();
        var generatedVideos = await videos.FindAsync(x => x.WorkflowRunId.HasValue && runIds.Contains(x.WorkflowRunId.Value) &&
            x.DeletedAt == null && x.Status == "completed" && x.StorageKey != null, ct);
        var versionsByRun = generatedVideos
            .GroupBy(x => x.WorkflowRunId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = ownedRuns
            .Where(run => versionsByRun.ContainsKey(run.Id))
            .Select(run => new VideoRunSummaryResponse(run.Id, run.Status, run.CreatedAt,
                versionsByRun[run.Id].Length, versionsByRun[run.Id].Max(video => video.VersionNumber)))
            .ToArray();
        return Result.Success<IReadOnlyList<VideoRunSummaryResponse>>(result);
    }
    public async Task<Result<RunResponse>> ActionAsync(Guid id, RunActionRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var run = await state.LockRunAsync(id, ct);
        if (run is null || run.UserId != user.UserId) return Result.Failure<RunResponse>(Missing());
        if (run.Revision != request.ExpectedRevision) return Result.Failure<RunResponse>(Stale());
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == id, ct);
        // A run that only writes a listing has none of the video steps the actions below work on.
        if (request.Action is "resume_mockups" or "approve_video" or "reject_video" or "rerender" or "retry" or "regenerate_ai_shot" &&
            checkpoints.All(x => x.NodeType != "generate-video"))
            return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "This run has no video step."));
        if (request.Action == "cancel")
        {
            if (run.Status is "completed" or "cancelled") return Result.Failure<RunResponse>(Error.Conflict("RunFinished", "Run is already finished."));
            run.Status = "cancelled"; run.CompletedAt = time.GetUtcNow().UtcDateTime;
            foreach (var n in checkpoints.Where(x => x.Status != "succeeded")) n.Status = "cancelled";
            foreach (var job in await jobs.FindAsync(x => x.WorkflowRunId == id && (x.Status == "queued" || x.Status == "leased"), ct)) { job.Status = "cancelled"; job.LeaseToken = null; }
        }
        else if (request.Action == "resume_mockups")
        {
            if (run.Status != "waiting_for_input") return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Run is not waiting for mockups."));
            var gate = checkpoints.Single(x => x.NodeType == "approval-gate");
            var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
            var config = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
            var generate = checkpoints.Single(x => x.NodeType == "generate-video");
            var videoConfig = WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
            var approved = await runtime.ApprovedAssetsAsync(run, config, videoConfig, ct);
            if (approved.Count == 0) return Result.Failure<RunResponse>(Error.Conflict("WaitingForInput", "Select and approve at least one current mockup or keyframe."));
            var consent = await quotes.VerifyAsync(run, videoConfig, request.AiShotQuoteToken, false, null, ct);
            if (consent.IsFailure) return Result.Failure<RunResponse>(consent.Error);
            gate.Status = "succeeded"; gate.OutputSnapshot = WorkflowJson.Write(new { mockupIds = approved.Select(x => x.Id), revisions = approved.Select(x => x.MetadataRevision) });
            run.Status = "running"; await runtime.AdvanceAsync(run, ct);
        }
        else if (request.Action == "retry_listing")
        {
            if (checkpoints.SingleOrDefault(x => x.NodeType == "generate-listing") is not { Status: "failed" })
                return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Listing content is not waiting for a retry."));
            // Writing the listing keeps this run locked while Gemini answers, and a render in progress has to
            // renew its lease against the same run in the meantime.
            if (run.Status is "running" or "cancelled")
                return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Retry the listing once the video step is waiting for you or has finished."));
            await runtime.RetryListingAsync(run, checkpoints, ct);
        }
        else if (request.Action is "approve_video" or "reject_video")
        {
            if (run.Status != "waiting_for_review") return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Run is not waiting for video review."));
            var review = checkpoints.Single(x => x.NodeType == "review-video");
            var expected = WorkflowJson.Read<VideoPointer>(review.OutputSnapshot);
            var video = request.VideoId.HasValue ? await videos.GetByIdAsync(request.VideoId.Value, ct) : null;
            if (video == null || video.Id != expected.VideoId || video.WorkflowRunId != id || video.ReviewRevision != request.ReviewRevision || video.Status != "completed")
                return Result.Failure<RunResponse>(Stale());
            video.ApprovalStatus = request.Action == "approve_video" ? "approved" : "rejected";
            video.ReviewRevision++; video.ApprovedBy = request.Action == "approve_video" ? user.UserId : null;
            video.ApprovedAt = request.Action == "approve_video" ? time.GetUtcNow().UtcDateTime : null;
            review.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id));
            if (request.Action == "approve_video") { review.Status = "succeeded"; run.Status = "running"; await runtime.AdvanceAsync(run, ct); }
            else { review.Status = "waiting_for_review"; run.Status = "waiting_for_review"; }
        }
        else if (request.Action is "rerender" or "retry" or "regenerate_ai_shot")
        {
            if (run.Status is not ("waiting_for_review" or "failed" or "waiting_for_input")) return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Cancel an active render before changing configuration."));
            var generate = checkpoints.Single(x => x.NodeType == "generate-video");
            if (request.Config != null)
            {
                if (!capabilities.IsModeEnabled(request.Config.Mode)) return Result.Failure<RunResponse>(new("UnsupportedVideoMode", "This video mode is unavailable.", ErrorType.Validation));
                var validation = await validator.ValidateAsync(request.Config, ct);
                if (!validation.IsValid) return Result.Failure<RunResponse>(Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage))));
            }
            if (request.Action is "rerender" or "regenerate_ai_shot" && request.ExpectedStoryboardFingerprint != null)
            {
                var candidateConfig = request.Config ?? WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
                var preview = await runtime.PlanAsync(run, candidateConfig, ct);
                if (preview.IsFailure) return Result.Failure<RunResponse>(preview.Error);
                if (!string.Equals(preview.Value.Fingerprint, request.ExpectedStoryboardFingerprint, StringComparison.Ordinal))
                    return Result.Failure<RunResponse>(Error.Conflict("StoryboardChanged", "Storyboard changed. Preview again before rendering."));
            }
            var selectedConfig = request.Config ?? WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
            var failedExportOnly = request.Action == "retry" && checkpoints.Single(x => x.NodeType == "export-zip").Status == "failed";
            if (!failedExportOnly)
            {
                if (request.Action == "regenerate_ai_shot" && selectedConfig.Mode != "ai_shot")
                    return Result.Failure<RunResponse>(Error.Validation("Regeneration requires AI Shot mode."));
                if (request.SceneOrder.HasValue && request.Action != "regenerate_ai_shot")
                    return Result.Failure<RunResponse>(Error.Validation("A scene can only be selected for AI clip regeneration."));
                var consent = await quotes.VerifyAsync(run, selectedConfig, request.AiShotQuoteToken, request.Action == "regenerate_ai_shot", request.SceneOrder, ct);
                if (consent.IsFailure) return Result.Failure<RunResponse>(consent.Error);
            }
            var failedExport = checkpoints.Single(x => x.NodeType == "export-zip");
            if (failedExportOnly) failedExport.Status = "pending";
            else
            {
                // A retry sends the settings on the canvas too. Only settings that differ from the run's make a new
                // video version; the same ones carry on with the version that failed.
                var configChanged = request.Config != null &&
                    WorkflowJson.Write(request.Config) != WorkflowJson.Write(WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot));
                if (request.Config != null) generate.InputSnapshot = WorkflowJson.Write(request.Config);
                generate.Status = "pending"; if (request.Action is "rerender" or "regenerate_ai_shot" || configChanged) generate.Attempt++; generate.OutputSnapshot = "{}"; generate.ErrorMessage = null;
                if (request.Action == "regenerate_ai_shot")
                {
                    var previousJob = (await jobs.FindAsync(x => x.WorkflowNodeRunId == generate.Id, ct)).SingleOrDefault();
                    if (previousJob?.Kind == "render" && request.SceneOrder.HasValue)
                    {
                        try
                        {
                            var payload = WorkflowJson.Read<AiRenderJobPayload>(previousJob.Payload);
                            previousJob.Payload = WorkflowJson.Write(payload with { AiShotStates = payload.AiShotStates.Select(item =>
                                item.SceneOrder == request.SceneOrder.Value ? new AiShotJobState(item.SceneOrder, item.Fingerprint) : item).ToArray() });
                        }
                        catch (JsonException) { previousJob.Payload = "{}"; }
                    }
                    else if (previousJob?.Kind == "render") previousJob.Payload = "{}";
                }
                checkpoints.Single(x => x.NodeType == "review-video").Status = "pending"; failedExport.Status = "pending";
            }
            foreach (var job in await jobs.FindAsync(x => x.WorkflowRunId == id && x.Status == "failed", ct)) job.Status = "cancelled";
            run.Status = "running"; run.ErrorMessage = null; await runtime.AdvanceAsync(run, ct);
        }
        else return Result.Failure<RunResponse>(Error.Validation("Unknown run action."));
        run.Revision++; run.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success(await MapAsync(run, ct));
    }
    private async Task<RunResponse> MapAsync(WorkflowRun run, CancellationToken ct) => new(run.Id, run.WorkflowId, run.ProductId, run.Status,
        run.Revision, run.WorkflowRevision, run.CreatedAt, run.ErrorMessage,
        (await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct)).Select(x => new NodeRunResponse(x.Id, x.NodeId, x.NodeType, x.Status, x.Attempt, x.Stage, x.Progress, x.ErrorMessage, JsonSerializer.Deserialize<JsonElement>(x.OutputSnapshot))).ToArray());
    private static Error Missing() => Error.NotFound("WorkflowRunNotFound", "Workflow run not found.");
    private static Error Stale() => Error.Conflict("StaleRevision", "The workflow or video changed. Reload before continuing.");
}
public sealed record VideoPointer(Guid VideoId);

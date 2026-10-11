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
    Task<Result<RunResponse>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<RunResponse>> ActionAsync(Guid id, RunActionRequest request, CancellationToken ct);
}

public sealed class WorkflowRunService(ICurrentUser user, IRepository<Workflow> workflows, IRepository<WorkflowRun> runs,
    IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<PromoVideo> videos, IRepository<MediaJob> jobs, IWorkflowStateRepository state, IUnitOfWork uow,
    WorkflowGraphValidator graph, WorkflowRuntime runtime, GenerateVideoConfigValidator validator,
    WorkflowCapabilityRegistry capabilities, TimeProvider time) : IWorkflowRunService
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
    public async Task<Result<RunResponse>> ActionAsync(Guid id, RunActionRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var run = await state.LockRunAsync(id, ct);
        if (run is null || run.UserId != user.UserId) return Result.Failure<RunResponse>(Missing());
        if (run.Revision != request.ExpectedRevision) return Result.Failure<RunResponse>(Stale());
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == id, ct);
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
            var approved = await runtime.ApprovedAssetsAsync(run, config, ct);
            if (approved.Count == 0) return Result.Failure<RunResponse>(Error.Conflict("WaitingForInput", "Select and approve at least one current mockup."));
            gate.Status = "succeeded"; gate.OutputSnapshot = WorkflowJson.Write(new { mockupIds = approved.Select(x => x.Id), revisions = approved.Select(x => x.MetadataRevision) });
            run.Status = "running"; await runtime.AdvanceAsync(run, ct);
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
        else if (request.Action is "rerender" or "retry")
        {
            if (run.Status is not ("waiting_for_review" or "failed" or "waiting_for_input")) return Result.Failure<RunResponse>(Error.Conflict("InvalidCheckpoint", "Cancel an active render before changing configuration."));
            var generate = checkpoints.Single(x => x.NodeType == "generate-video");
            if (request.Config != null)
            {
                if (!capabilities.IsModeEnabled(request.Config.Mode)) return Result.Failure<RunResponse>(new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation));
                var validation = await validator.ValidateAsync(request.Config, ct);
                if (!validation.IsValid) return Result.Failure<RunResponse>(Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage))));
            }
            if (request.Action == "rerender" && request.ExpectedStoryboardFingerprint != null)
            {
                var candidateConfig = request.Config ?? WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
                var preview = await runtime.PlanAsync(run, candidateConfig, ct);
                if (preview.IsFailure) return Result.Failure<RunResponse>(preview.Error);
                if (!string.Equals(preview.Value.Fingerprint, request.ExpectedStoryboardFingerprint, StringComparison.Ordinal))
                    return Result.Failure<RunResponse>(Error.Conflict("StoryboardChanged", "Storyboard changed. Preview again before rendering."));
            }
            if (request.Config != null) generate.InputSnapshot = WorkflowJson.Write(request.Config);
            var failedExport = checkpoints.Single(x => x.NodeType == "export-zip");
            if (request.Action == "retry" && failedExport.Status == "failed") failedExport.Status = "pending";
            else
            {
                generate.Status = "pending"; if (request.Action == "rerender" || request.Config != null) generate.Attempt++; generate.OutputSnapshot = "{}"; generate.ErrorMessage = null;
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

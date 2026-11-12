using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant;

public interface IAssistantRunProgressReader
{
    Task<Result<AssistantRunProgressResponseDto>> ReadAsync(Guid workflowId, CancellationToken cancellationToken);
}

public sealed class AssistantRunProgressReader(
    IWorkflowService workflows,
    IWorkflowRunService runs,
    IWorkflowRunGroupService runGroups,
    IDesignGenerationService designJobs) : IAssistantRunProgressReader
{
    private const int MaxTargets = 10;
    private const string OnScreen = "Open the workflow screen to continue; it shows this step.";

    public async Task<Result<AssistantRunProgressResponseDto>> ReadAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var workflow = await workflows.GetAsync(workflowId, cancellationToken);
        if (workflow.IsFailure)
            return Result.Failure<AssistantRunProgressResponseDto>(workflow.Error);

        var name = workflow.Value.Name;
        var job = await LatestJobAsync(workflowId, workflow.Value.Definition, cancellationToken);
        var group = await LatestGroupAsync(workflowId, cancellationToken);
        var run = await LatestSingleRunAsync(workflowId, cancellationToken);

        var jobAt = job?.CreatedAt is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : (DateTimeOffset?)null;
        var latest = new[]
        {
            (Kind: "job", At: jobAt),
            (Kind: "group", At: group?.CreatedAt),
            (Kind: "run", At: run?.CreatedAt)
        }.Where(item => item.At is not null).OrderByDescending(item => item.At).FirstOrDefault();

        return latest.Kind switch
        {
            "job" => await FromJobAsync(workflowId, name, job!, jobAt, cancellationToken),
            "group" => await FromGroupAsync(workflowId, name, group!, cancellationToken),
            "run" => Result.Success(FromRun(workflowId, name, run!)),
            _ => Result.Success(new AssistantRunProgressResponseDto(workflowId, name, AssistantRunStages.NotStarted, AssistantRunStates.Idle,
                "The workflow has not run yet. Offer to run it."))
        };
    }

    private async Task<BatchJobSummaryDto?> LatestJobAsync(Guid workflowId, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var batchId = WorkflowGraphValidator.BatchIdOf(definition);
        if (batchId == Guid.Empty)
            return null;
        var jobs = await designJobs.ListJobsForBatchAsync(batchId, cancellationToken);
        return jobs.IsFailure
            ? null
            : jobs.Value
                .Where(item => item.WorkflowId == workflowId && item.Status != BatchJobStatuses.Draft)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();
    }

    private async Task<RunGroupSummaryResponse?> LatestGroupAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var groups = await runGroups.ListAsync(workflowId, cancellationToken);
        return groups.IsFailure ? null : groups.Value.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
    }

    private async Task<RunResponse?> LatestSingleRunAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var run = await runs.GetLatestAsync(workflowId, cancellationToken);
        return run.IsSuccess && run.Value.RunGroupId is null ? run.Value : null;
    }

    private async Task<Result<AssistantRunProgressResponseDto>> FromJobAsync(
        Guid workflowId, string name, BatchJobSummaryDto job, DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        var progress = new AssistantRunProgressResponseDto(workflowId, name, AssistantRunStages.Designs, AssistantRunStates.Working,
            "Designs are being generated. Nothing to do yet.", job.ProcessedProducts, job.TotalProducts, BatchJobId: job.Id, StartedAt: startedAt);

        if (BatchJobStatuses.Active.Contains(job.Status))
            return Result.Success(progress);
        if (job.Status == BatchJobStatuses.Failed)
        {
            return Result.Success(progress with
            {
                State = AssistantRunStates.Failed,
                NextStep = "Every design failed. The user can retry the failed products on the workflow screen.",
                Error = $"{job.FailedProducts} of {job.TotalProducts} products failed."
            });
        }

        var detail = await designJobs.GetJobAsync(job.Id, cancellationToken);
        var waiting = detail.IsSuccess
            ? detail.Value.Products.Count(product => product.Status == BatchJobProductStatuses.ImageReviewRequired)
            : 0;
        var failed = job.FailedProducts > 0 ? $"{job.FailedProducts} of {job.TotalProducts} products failed." : null;
        if (waiting > 0)
        {
            return Result.Success(progress with
            {
                Stage = AssistantRunStages.DesignApproval,
                State = AssistantRunStates.WaitingForUser,
                NextStep = $"Designs of {waiting} products wait for approval. They can be approved or rejected in this chat or on the workflow screen, then the run continues to the mock-ups.",
                WaitingCount = waiting,
                Error = failed
            });
        }

        return Result.Success(progress with
        {
            Stage = AssistantRunStages.DesignsReady,
            State = AssistantRunStates.WaitingForUser,
            NextStep = "Designs are reviewed. The run continues to the mock-ups with Continue, from this chat when the workflow is open or on the workflow screen.",
            Error = failed
        });
    }

    private async Task<Result<AssistantRunProgressResponseDto>> FromGroupAsync(
        Guid workflowId, string name, RunGroupSummaryResponse summary, CancellationToken cancellationToken)
    {
        var detail = await runGroups.GetAsync(summary.Id, cancellationToken);
        if (detail.IsFailure)
            return Result.Failure<AssistantRunProgressResponseDto>(detail.Error);

        var group = detail.Value;
        var counts = group.Progress;
        var targets = group.Targets
            .Where(target => target.Selected || group.Status == "waiting_for_targets")
            .Take(MaxTargets)
            .Select(target => new AssistantRunTargetResponseDto(target.DesignLabel, target.ProductName, target.RunStatus, target.CurrentStep, target.RunError, target.RunId))
            .ToArray();
        var progress = new AssistantRunProgressResponseDto(workflowId, name, AssistantRunStages.Video, AssistantRunStates.Working,
            "The videos are being made. Nothing to do yet.", counts.Completed, counts.Total,
            RunGroupId: group.Id, StartedAt: group.CreatedAt, Targets: targets);

        if (group.Status == "waiting_for_targets")
        {
            var pending = group.Targets.Sum(target => target.PendingCount);
            return Result.Success(progress with
            {
                Stage = AssistantRunStages.MockupApproval,
                State = AssistantRunStates.WaitingForUser,
                NextStep = $"Mock-ups wait for approval ({pending} not reviewed). The mock-ups of each design that should get a video are approved in this chat or on the workflow screen, then Continue starts the videos.",
                WaitingCount = pending,
                Done = null,
                Total = group.Targets.Count
            });
        }
        if (group.Status == "cancelled")
            return Result.Success(progress with { State = AssistantRunStates.Cancelled, NextStep = "The run was cancelled. Offer to run it again." });
        if (counts.WaitingForReview > 0)
        {
            return Result.Success(progress with
            {
                Stage = AssistantRunStages.VideoReview,
                State = AssistantRunStates.WaitingForUser,
                NextStep = $"{counts.WaitingForReview} videos wait for review. Each can be approved or rejected in this chat (the targets list their runs) or on the workflow screen.",
                WaitingCount = counts.WaitingForReview
            });
        }
        if (counts.WaitingForInput > 0)
        {
            return Result.Success(progress with
            {
                State = AssistantRunStates.WaitingForUser,
                NextStep = $"{counts.WaitingForInput} videos need input before they can render. {OnScreen}",
                WaitingCount = counts.WaitingForInput
            });
        }
        if (counts.Queued + counts.Running > 0)
            return Result.Success(progress);
        if (counts.Failed > 0)
        {
            return Result.Success(progress with
            {
                State = AssistantRunStates.Failed,
                NextStep = $"{counts.Failed} of {counts.Total} videos failed. The user can retry them on the workflow screen.",
                Error = targets.FirstOrDefault(target => target.Error is not null)?.Error
            });
        }
        return Result.Success(progress with { Stage = AssistantRunStages.Done, State = AssistantRunStates.Done, NextStep = "Every video is done. The export is ready on the workflow screen." });
    }

    private static AssistantRunProgressResponseDto FromRun(Guid workflowId, string name, RunResponse run)
    {
        var current = run.Nodes.FirstOrDefault(node => node.Status is "running" or "waiting_for_input" or "waiting_for_review" or "failed")
            ?? run.Nodes.FirstOrDefault(node => node.Status == "pending" && !WorkflowCapabilityRegistry.BatchSteps.Contains(node.NodeType));
        var stage = StageOf(current?.NodeType);
        var done = run.Nodes.Count(node => node.Status == "succeeded");
        var progress = new AssistantRunProgressResponseDto(workflowId, name, stage, AssistantRunStates.Working,
            "The run is working. Nothing to do yet.", done, run.Nodes.Count, RunId: run.Id, StartedAt: run.CreatedAt);

        return run.Status switch
        {
            "waiting_for_input" when current?.ErrorMessage is { } problem => progress with
            {
                State = AssistantRunStates.WaitingForUser,
                NextStep = $"The video needs a change before it can render. {OnScreen}",
                Error = problem
            },
            "waiting_for_input" => progress with
            {
                Stage = AssistantRunStages.MockupApproval,
                State = AssistantRunStates.WaitingForUser,
                NextStep = "Mock-ups wait for approval. They can be approved in this chat or on the workflow screen, then Continue starts the video."
            },
            "waiting_for_review" => progress with
            {
                Stage = AssistantRunStages.VideoReview,
                State = AssistantRunStates.WaitingForUser,
                NextStep = "The video waits for review. The user can approve or reject it here or on the workflow screen."
            },
            "failed" => progress with
            {
                State = AssistantRunStates.Failed,
                NextStep = "A step failed. Offer to retry it.",
                Error = current?.ErrorMessage ?? run.ErrorMessage
            },
            "cancelled" => progress with { State = AssistantRunStates.Cancelled, NextStep = "The run was cancelled. Offer to run it again." },
            "completed" => progress with { Stage = AssistantRunStages.Done, State = AssistantRunStates.Done, NextStep = "The run is done." },
            _ => progress
        };
    }

    private static string StageOf(string? nodeType) => nodeType switch
    {
        "approval-gate" or "apply-mockup" => AssistantRunStages.MockupApproval,
        "generate-video" => AssistantRunStages.Video,
        "review-video" => AssistantRunStages.VideoReview,
        "generate-listing" => AssistantRunStages.Listing,
        "export-zip" => AssistantRunStages.Export,
        "publish-printify" or "publish-etsy" => AssistantRunStages.Publish,
        _ => AssistantRunStages.Video
    };
}

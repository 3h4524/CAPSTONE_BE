namespace APCS.Application.Features.Workflows;

/// <summary>Starts the video part of a batch run: one video per design the seller confirms at Mockup Approval.</summary>
/// <param name="BatchJobId">The design job whose products the videos are for; without it, every product of the batch.</param>
/// <param name="AiBudgetUsd">
/// The AI video cost the seller confirmed when pressing Run (<see cref="RunCostEstimateResponse.AiVideoBudgetUsd"/>).
/// With Mockup Approval on auto, the videos start without asking again when their exact quote is within it.
/// </param>
public sealed record StartRunGroupRequest(long ExpectedWorkflowRevision, string IdempotencyKey, Guid? BatchJobId = null, decimal? AiBudgetUsd = null);

/// <param name="AiShotQuoteToken">
/// Confirms what the AI clips of every design cost, from the run group's quote; Standard videos need none.
/// </param>
public sealed record RunGroupRevisionRequest(long ExpectedRevision, string? AiShotQuoteToken = null);

/// <summary>The scene images one design uses instead of the ones the shared scene list resolves to.</summary>
/// <param name="Scenes">Empty or null puts the design back on the shared scene list.</param>
public sealed record UpdateTargetOverrideRequest(long ExpectedRevision, Guid ProductId, string ArtworkGroupKey, IReadOnlyList<SceneMockupOverride>? Scenes);

public sealed record SceneMockupOverride(int SceneIndex, Guid MockupId);

/// <summary>What a run group stores per design before rendering.</summary>
/// <param name="Scenes">Images picked for single scenes of the scene list the design uses.</param>
/// <param name="Config">
/// The design's own Generate Video settings (scene list, duration, format...), used instead of the workflow's shared
/// ones; null keeps the shared settings. Its images are the design's own; the scene picks above still apply.
/// </param>
public sealed record TargetOverride(IReadOnlyList<SceneMockupOverride> Scenes, GenerateVideoConfig? Config = null);

/// <summary>The whole Generate Video configuration of one design before its video starts.</summary>
/// <param name="Config">Null puts the design back on the workflow's shared settings.</param>
public sealed record UpdateTargetConfigRequest(long ExpectedRevision, Guid ProductId, string ArtworkGroupKey, GenerateVideoConfig? Config);

public sealed record RunGroupResponse(Guid Id, Guid WorkflowId, Guid BatchId, Guid? BatchJobId, string Status, long Revision,
    long WorkflowRevision, DateTimeOffset CreatedAt, int MaximumTargets, int SelectedCount, RunGroupProgress Progress,
    IReadOnlyList<RunGroupTargetResponse> Targets);

/// <summary>How many child runs are in each state; all zero before the designs are confirmed.</summary>
public sealed record RunGroupProgress(int Total, int Queued, int Running, int WaitingForReview, int Completed, int Failed, int Cancelled, int WaitingForInput);

/// <summary>
/// One design of one product: the mock-ups it has, whether it gets a video, how its scenes resolve and, once
/// confirmed, the child run that makes its video.
/// </summary>
/// <param name="Config">
/// The design's Generate Video settings: its own ones before the videos start (null while it follows the shared
/// ones), then the settings its run renders with.
/// </param>
/// <param name="HasOwnConfig">The design has settings of its own instead of the workflow's shared ones.</param>
public sealed record RunGroupTargetResponse(string Key, Guid ProductId, string ProductName, string ProductType, string ArtworkGroupKey,
    string DesignLabel, string? PreviewUrl, int MockupCount, int ApprovedCount, int PendingCount, bool Selected, bool HasOverride,
    IReadOnlyList<RunGroupScenePreview> Scenes, IReadOnlyList<string> Warnings, string? ResolveError,
    Guid? RunId, string? RunStatus, string? CurrentStep, string? Stage, int Progress, string? RunError,
    GenerateVideoConfig? Config = null, bool HasOwnConfig = false);

public sealed record RunGroupScenePreview(int SceneIndex, Guid? MockupId, string? PreviewUrl, string? Warning, bool Overridden);

/// <param name="BatchJobId">The design job whose designs the group made videos of, so reopening that job finds its videos again.</param>
public sealed record RunGroupSummaryResponse(Guid Id, string Status, DateTimeOffset CreatedAt, int TargetCount, int CompletedCount, Guid? BatchJobId = null);

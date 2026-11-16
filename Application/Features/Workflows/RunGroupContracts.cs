namespace APCS.Application.Features.Workflows;

/// <summary>Starts the video part of a batch run: one video per design the seller confirms at Mockup Approval.</summary>
/// <param name="BatchJobId">The design job whose products the videos are for; without it, every product of the batch.</param>
/// <remarks>
/// The seller saw what the Run costs before pressing it (<see cref="RunCostEstimateResponse"/>): model photos and AI
/// clips are then made without a second confirmation.
/// </remarks>
public sealed record StartRunGroupRequest(long ExpectedWorkflowRevision, string IdempotencyKey, Guid? BatchJobId = null);

/// <param name="AiShotQuoteToken">Not required: kept so a client that still sends the token of a quote is accepted.</param>
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

/// <param name="Notice">
/// Why a run set to start by itself is waiting for the seller (the cost to confirm, a plan limit...), or which designs
/// it left out; null when there is nothing to say.
/// </param>
/// <param name="Preparing">
/// The run is being made ready in the background (the model photos of its scenes with a person, then the automatic
/// start of its videos): nothing for the seller to do yet.
/// </param>
public sealed record RunGroupResponse(Guid Id, Guid WorkflowId, Guid BatchId, Guid? BatchJobId, string Status, long Revision,
    long WorkflowRevision, DateTimeOffset CreatedAt, int MaximumTargets, int SelectedCount, RunGroupProgress Progress,
    IReadOnlyList<RunGroupTargetResponse> Targets, string? Notice = null, bool Preparing = false);

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
/// <param name="VideoStatus">
/// Where the design's video is, as a run status (<c>queued</c>, <c>running</c>, <c>waiting_for_review</c>,
/// <c>completed</c>, <c>failed</c>...), apart from Push to Printify: Printify runs beside the video, so a Printify that
/// failed leaves the run failed while its video is done. Null before the design has a run, or for a run without a video.
/// </param>
/// <param name="Printify">
/// Where the design's Push to Printify step is, apart from the video beside it. Null before the design has a run, or
/// for a workflow without that step.
/// </param>
public sealed record RunGroupTargetResponse(string Key, Guid ProductId, string ProductName, string ProductType, string ArtworkGroupKey,
    string DesignLabel, string? PreviewUrl, int MockupCount, int ApprovedCount, int PendingCount, bool Selected, bool HasOverride,
    IReadOnlyList<RunGroupScenePreview> Scenes, IReadOnlyList<string> Warnings, string? ResolveError,
    Guid? RunId, string? RunStatus, string? CurrentStep, string? Stage, int Progress, string? RunError,
    GenerateVideoConfig? Config = null, bool HasOwnConfig = false, string? VideoStatus = null, RunGroupStepResponse? Printify = null);

/// <summary>One step of a design's run: its status (<c>pending</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c>...), stage, progress and error.</summary>
public sealed record RunGroupStepResponse(string Status, string? Stage, int Progress, string? Error);

public sealed record RunGroupScenePreview(int SceneIndex, Guid? MockupId, string? PreviewUrl, string? Warning, bool Overridden);

/// <param name="BatchJobId">The design job whose designs the group made videos of, so reopening that job finds its videos again.</param>
public sealed record RunGroupSummaryResponse(Guid Id, string Status, DateTimeOffset CreatedAt, int TargetCount, int CompletedCount, Guid? BatchJobId = null);

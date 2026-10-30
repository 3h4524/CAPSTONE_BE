using System.Text.Json;

namespace APCS.Application.Features.Workflows;

public static class WorkflowJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidOperationException("Invalid persisted workflow data.");
    public static T Config<T>(WorkflowNode node) => node.Config.Deserialize<T>(Options)
        ?? throw new JsonException("Missing node configuration.");
    public static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record Point(double X = .5, double Y = .5);
public sealed record Region(double X, double Y, double Width, double Height);
public sealed record MockupRegions(Point? FocalPoint = null, Region? Product = null, Region? Artwork = null,
    Region? Detail = null, AiStudioFrameMetadata? StudioFrame = null);
public sealed record WorkflowNode(string Id, string Type, string Label, Point Position, JsonElement Config);
public sealed record WorkflowEdge(string Id, string Source, string Target);
public sealed record WorkflowDefinition(int Version, IReadOnlyList<WorkflowNode> Nodes, IReadOnlyList<WorkflowEdge> Edges, JsonElement? Viewport = null);
public sealed record SaveWorkflowRequest(string Name, string Description, WorkflowDefinition Definition, long? ExpectedRevision = null);
public sealed record WorkflowResponse(Guid Id, string Name, string Description, int NodeCount, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, WorkflowDefinition Definition);
public sealed record ProductInputConfig(Guid BatchId, Guid ProductId);
public sealed record MockupConfig(IReadOnlyList<Guid>? MockupIds = null, string? ArtworkGroupKey = null);
public sealed record StandardOptions(string MotionPreset = "zoom", string Crop = "safe", string Transition = "none");
public sealed record StandardSceneOptions(Guid MockupId, Region? StartCrop = null, Region? EndCrop = null, Region? BaseCrop = null,
    double StartScale = 1, double EndScale = 1, double StartRotation = 0, double EndRotation = 0);
public sealed record AiVideoSceneOptions(
    Guid? MockupId = null,
    string TemplateCode = "cinematic_product_showcase",
    int DurationSeconds = 4,
    string Framing = "medium",
    string CameraAngle = "three_quarter",
    string CameraMovement = "push_in",
    string SubjectMotion = "none",
    string MotionIntensity = "subtle",
    string Lighting = "soft_studio",
    string? ExtraInstructions = null,
    bool RightsConfirmed = false,
    string Mode = "ai",
    string? StartFrameMode = null,
    string? StudioLayout = null,
    Guid? StudioFrameId = null,
    string? StudioPrompt = null,
    IReadOnlyList<string>? StudioReferenceHashes = null,
    string? StandardMotionPreset = null,
    Region? StandardBaseCrop = null,
    Region? StandardStartCrop = null,
    Region? StandardEndCrop = null,
    double? StandardStartScale = null,
    double? StandardEndScale = null,
    double? StandardStartRotation = null,
    double? StandardEndRotation = null,
    int? DisplayDurationFrames = null,
    int? TrimStartFrames = null,
    int? TrimEndFrames = null,
    string? TransitionIn = null,
    int? TransitionDurationFrames = null,
    int? PromptRuleVersion = null,
    string? StartingState = null,
    string? PrimaryAction = null,
    string? EndingState = null);
public sealed record VideoOverlay(string Id, string Type, string Text, int StartFrame, int EndFrame, double X, double Y,
    string Align, string Preset, double FontScale, string TextColor, string BackgroundColor);
public sealed record VideoMusicConfig(Guid TrackId, int StartFrame = 0, int SourceOffsetFrames = 0, double Volume = .25,
    int FadeInFrames = 15, int FadeOutFrames = 15, bool Loop = true, bool Muted = false);
public sealed record VideoEditConfig(int Version = 1, VideoMusicConfig? Music = null, IReadOnlyList<VideoOverlay>? Overlays = null);
public sealed record VideoOutputSpec(string Format, int Width, int Height, string AspectRatio);
public static class VideoOutputFormats
{
    public const string Default = "portrait";
    public static bool IsSupported(string format) => format is "square" or "portrait" or "landscape";
    public static VideoOutputSpec Resolve(string? format) => format switch
    {
        "square" => new("square", 1080, 1080, "1:1"),
        "portrait" => new("portrait", 1080, 1920, "9:16"),
        "landscape" => new("landscape", 1920, 1080, "16:9"),
        _ => new(Default, 1080, 1920, "9:16"),
    };
}
public sealed record AiShotOptions(string Coverage = "single_scene", string Concept = "product_cinematic", int SceneIndex = 0,
    Guid? MockupId = null, string? Prompt = null, bool RightsConfirmed = false, IReadOnlyList<Guid>? FullSceneMockupIds = null);
public sealed record GenerateVideoConfig(
    string Mode = "standard", string Target = "etsy", string Template = "auto", int DurationSeconds = 12,
    string AssetSelection = "automatic", IReadOnlyList<Guid>? SelectedMockupIds = null,
    IReadOnlyList<Guid>? SceneOrder = null, string TextOverlay = "", StandardOptions? StandardOptions = null,
    AiShotOptions? AiShotOptions = null, bool FallbackToStandard = true,
    IReadOnlyList<string?>? SceneMotionPresets = null, int? TemplateVersion = null, string OutputFormat = VideoOutputFormats.Default,
    IReadOnlyList<AiVideoSceneOptions>? AiScenes = null, string? AiGlobalInstructions = null,
    string AiStudioMode = "direct", string AiStudioLayout = "hanging_window",
    Guid? AiStudioSourceMockupId = null, Guid? AiStudioFrameId = null,
    IReadOnlyList<StandardSceneOptions>? StandardScenes = null, VideoEditConfig? VideoEdit = null);
public sealed record VideoTemplateRequirements(int MinimumAssets, bool RequiresDetail, int MinimumVariants);
public sealed record VideoTemplateCatalogItem(string Code, int Version, string Name, string Description,
    string? PreviewVideoUrl, int DefaultDurationSeconds, string DefaultTransition, string DefaultMotionPreset,
    VideoTemplateRequirements Requirements);
public sealed record CostRange(string Currency, decimal Minimum, decimal Maximum, string Note);
public sealed record VideoModeCapability(string Mode, bool Enabled, string Availability, string Description, bool SupportsFallback, CostRange? EstimatedCostRange = null);
public sealed record NodeCapability(string Type, bool Enabled, string Description);
public sealed record WorkflowCapabilities(int DefinitionVersion, IReadOnlyList<NodeCapability> Nodes, IReadOnlyList<VideoModeCapability> VideoModes);
public sealed record StartRunRequest(long ExpectedWorkflowRevision, string IdempotencyKey);
public sealed record RunActionRequest(long ExpectedRevision, string Action, Guid? VideoId = null, long? ReviewRevision = null,
    GenerateVideoConfig? Config = null, string? ExpectedStoryboardFingerprint = null, string? AiShotQuoteToken = null,
    int? SceneOrder = null);
public sealed record NodeRunResponse(Guid Id, string NodeId, string NodeType, string Status, int Attempt, string? Stage, int Progress, string? ErrorMessage, JsonElement Output);
public sealed record RunResponse(Guid Id, Guid WorkflowId, Guid ProductId, string Status, long Revision, long WorkflowRevision, DateTimeOffset CreatedAt, string? ErrorMessage, IReadOnlyList<NodeRunResponse> Nodes);
/// <summary>Identifies a previous workflow run that still has generated video versions available.</summary>
public sealed record VideoRunSummaryResponse(Guid Id, string Status, DateTimeOffset CreatedAt, int VideoCount, int LatestVersion);
public sealed record MockupMetadataRequest(long ExpectedRevision, string Role, string ArtworkGroupKey, string? VariantKey, MockupRegions Regions);
public sealed record MockupReviewRequest(long ExpectedRevision, bool Approved);
public sealed record MockupResponse(Guid Id, Guid ProductId, string SourceType, string Role, string? ArtworkGroupKey, string? VariantKey, long Revision, string ApprovalStatus, long? ApprovedRevision, int Width, int Height, MockupRegions Regions, string PreviewUrl, IReadOnlyList<string> Warnings);
public sealed record AiStudioFrameReferenceInput(byte[] Bytes, string MimeType);
public sealed record AiStudioFrameRequest(Guid SourceMockupId, string Layout, string OutputFormat, bool Regenerate = false,
    string? ExpectedFingerprint = null, decimal? ConfirmedEstimatedCostUsd = null, string? CustomPrompt = null,
    string? TemplateCode = null, IReadOnlyList<AiStudioFrameReferenceInput>? References = null,
    bool ReferenceRightsConfirmed = false);
public sealed record AiStudioFrameQuote(string Fingerprint, int PromptVersion, decimal EstimatedCostUsd,
    MockupResponse? ExistingFrame, string Note);
public sealed record AiStudioFrameGenerationResponse(MockupResponse Frame, bool Reused, decimal EstimatedCostUsd);
/// <summary>One readable section of the exact prompt assembled for an AI scene.</summary>
public sealed record AiPromptPart(string Key, string Label, string Content, bool IncludedInFinalPrompt);
/// <summary>A verified, owner-scoped lesson selected for one AI video scene.</summary>
public sealed record VideoPromptExperience(Guid LessonId, string? Revision, string Content);
/// <summary>Explains the prompt supplied to the provider without exposing a separate prompt-building endpoint.</summary>
public sealed record AiPromptTrace(int? RuleVersion, string RagStatus, IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> Warnings, IReadOnlyList<AiPromptPart> Parts, string FinalPrompt,
    IReadOnlyList<VideoPromptExperience>? RetrievedExperiences = null);
public sealed record ScenePlan(Guid MockupId, long SourceRevision, string SourceHash, string Role, int SceneOrder, int DurationFrames, Region Crop, Region EndCrop, string Motion, string Transition, string Text, string GenerationStrategy, IReadOnlyList<string> Warnings,
    string? AiTemplateCode = null, int? PromptVersion = null, string? CompiledPrompt = null, AiVideoSceneOptions? AiSettings = null, Region? SourceCrop = null,
    string? AiProviderKey = null, string? AiModelKey = null, string? AiModelName = null, double StartScale = 1, double EndScale = 1, double StartRotation = 0, double EndRotation = 0,
    int TrimStartFrames = 0, int TrimEndFrames = 0, int TransitionFrames = 0, string? AiResolution = null, decimal? AiPricePerSecondUsd = null,
    AiPromptTrace? AiPromptTrace = null);
public sealed record AiShotPlan(int SceneOrder, Guid MockupId, string SourceHash, string Model, string Preset, string Prompt, int DurationSeconds, string Fingerprint,
    string ProviderKey = VeoVideoModel.ProviderKey, string ModelKey = VeoVideoModel.ModelKey, int PromptVersion = 1, Region? SourceCrop = null,
    string Resolution = VeoVideoModel.Resolution, decimal PricePerSecondUsd = VeoVideoModel.PricePerSecondUsd);
public sealed record Storyboard(string Template, int TemplateVersion, int DurationSeconds, string ProductType, IReadOnlyList<ScenePlan> Scenes, string Fingerprint,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 1920, string AspectRatio = "9:16",
    string Mode = "standard", int TransitionOverlapFrames = 0, IReadOnlyList<AiShotPlan>? AiShots = null, int? OutputDurationFrames = null,
    VideoEditConfig? VideoEdit = null);
public sealed record VideoResponse(Guid Id, Guid RunId, string Mode, string Template, int Version, long ReviewRevision, string Status, string ApprovalStatus, string? PreviewUrl, string? ThumbnailUrl, JsonElement Qa, IReadOnlyList<ScenePlan> Scenes,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 1920, string AspectRatio = "9:16");
public sealed record AiSceneVideoResponse(int SceneOrder, string Status, string? PreviewUrl, string? Failure);
/// <summary>Evidence produced by the locally configured OCR and VBench tools for one scene.</summary>
public sealed record VideoSceneEvaluationResponse(string Status, DateTimeOffset? EvaluatedAtUtc,
    string EvaluatorVersion, IReadOnlyDictionary<string, object?> Metrics, string? Detail = null);
/// <summary>A suggestion produced from explicit review facts; the seller always confirms the target.</summary>
public sealed record CorrectionSuggestionResponse(string Target, string Reason, string Confidence);
/// <summary>One timestamped observation made by the seller while reviewing a scene.</summary>
public sealed record VideoSceneReviewResponse(Guid Id, int StartMilliseconds, int EndMilliseconds,
    string Category, string Severity, bool PresentInSourceImage, string Description, string Status,
    CorrectionSuggestionResponse Suggestion, string? ConfirmedTarget, Guid? LessonId,
    string? LessonStatus, string? LessonRevision, string? LessonContent,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
/// <summary>All quality evidence and seller observations saved for a single scene.</summary>
public sealed record VideoSceneQualityResponse(Guid VideoId, int SceneOrder,
    VideoSceneEvaluationResponse? Evaluation, IReadOnlyList<VideoSceneReviewResponse> Reviews);
/// <summary>Creates or updates one seller observation for a scene.</summary>
public sealed record SaveVideoSceneReviewRequest(int StartMilliseconds, int EndMilliseconds, string Category,
    string Severity, bool PresentInSourceImage, string Description, bool ConfirmSuggestion = false,
    string? TargetOverride = null, string? CandidateLesson = null, Guid? ReviewId = null,
    bool SuspectRagExperience = false);
/// <summary>Changes a candidate lesson after the seller has reviewed its text and evidence.</summary>
public sealed record SetVideoExperienceStatusRequest(string Status);
/// <summary>Creates a new candidate revision from an existing private video lesson.</summary>
public sealed record ReviseVideoExperienceRequest(Guid ReviewId, string Content);
/// <summary>Returns the lifecycle state of a lesson that originated from a reviewed video scene.</summary>
public sealed record VideoExperienceResponse(Guid LessonId, string Status, string? Revision, string Content);
public sealed record WorkerAsset(Guid MockupId, string Url, string FileName);
public sealed record UploadGrant(string Url, IReadOnlyDictionary<string, string> Fields, string StorageKey, string ResourceType);
public sealed record WorkerJob(Guid Id, Guid LeaseToken, string Kind, JsonElement Payload, IReadOnlyList<WorkerAsset> Assets, IReadOnlyDictionary<string, UploadGrant> Uploads);
public sealed record JobHeartbeat(Guid LeaseToken, string Stage, int Progress);
public sealed record MediaQa(string Codec, string PixelFormat, int Width, int Height, double Fps, double DurationSeconds, long Bytes, bool HasAudio, bool FullDecode);
public sealed record JobCompletion(Guid LeaseToken, string StorageKey, string StorageVersion, string? ThumbnailStorageKey, string? ThumbnailStorageVersion, MediaQa? Qa);
public sealed record JobFailure(Guid LeaseToken, string Message);
public sealed record RenderJobPayload(Guid VideoId, Storyboard Storyboard);
/// <param name="Failure">Why the scene has no clip, when Google answered without one.</param>
/// <param name="Resubmissions">How often the scene was sent again after Google dropped a clip without charging for it.</param>
public sealed record AiShotJobState(int SceneOrder, string Fingerprint, string? OperationName = null, string? DownloadUri = null,
    string? StorageKey = null, string? StorageVersion = null, string Status = "pending", string? Failure = null, int Resubmissions = 0);
public sealed record AiRenderJobPayload(Guid VideoId, Storyboard Storyboard, IReadOnlyList<AiShotJobState> AiShotStates);
/// <summary>Protected prompt snapshot written after quote confirmation and consumed once by the render step.</summary>
public sealed record AiShotPromptSnapshot(string ConfigFingerprint, Storyboard Storyboard);
public sealed record ExportJobPayload(Guid ExportId, Guid VideoId, string VideoStorageKey, string VideoStorageVersion, string ThumbnailStorageKey, string ThumbnailStorageVersion, JsonElement Manifest);

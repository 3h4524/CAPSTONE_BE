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
public sealed record MockupRegions(Point? FocalPoint = null, Region? Product = null, Region? Artwork = null, Region? Detail = null);
public sealed record WorkflowNode(string Id, string Type, string Label, Point Position, JsonElement Config);
public sealed record WorkflowEdge(string Id, string Source, string Target);
public sealed record WorkflowDefinition(int Version, IReadOnlyList<WorkflowNode> Nodes, IReadOnlyList<WorkflowEdge> Edges, JsonElement? Viewport = null);
public sealed record SaveWorkflowRequest(string Name, string Description, WorkflowDefinition Definition, long? ExpectedRevision = null);
public sealed record WorkflowResponse(Guid Id, string Name, string Description, int NodeCount, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, WorkflowDefinition Definition);
public sealed record ProductInputConfig(Guid BatchId, Guid ProductId);
public sealed record MockupConfig(IReadOnlyList<Guid>? MockupIds = null, string? ArtworkGroupKey = null);
public sealed record StandardOptions(string MotionPreset = "gentle", string Crop = "safe", string Transition = "fade");
public sealed record AiVideoSceneOptions(
    Guid? MockupId = null,
    string TemplateCode = "cinematic_product_showcase",
    int DurationSeconds = 4,
    string Framing = "medium",
    string CameraAngle = "three_quarter",
    string CameraMovement = "push_in",
    string SubjectMotion = "minimal",
    string MotionIntensity = "subtle",
    string Lighting = "soft_studio",
    string? ExtraInstructions = null,
    bool RightsConfirmed = false,
    string Mode = "ai",
    string? StartFrameMode = null,
    string? StudioLayout = null,
    Guid? StudioFrameId = null,
    string? StudioPrompt = null);
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
    Guid? AiStudioSourceMockupId = null, Guid? AiStudioFrameId = null);
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
    GenerateVideoConfig? Config = null, string? ExpectedStoryboardFingerprint = null, string? AiShotQuoteToken = null);
public sealed record NodeRunResponse(Guid Id, string NodeId, string NodeType, string Status, int Attempt, string? Stage, int Progress, string? ErrorMessage, JsonElement Output);
public sealed record RunResponse(Guid Id, Guid WorkflowId, Guid ProductId, string Status, long Revision, long WorkflowRevision, DateTimeOffset CreatedAt, string? ErrorMessage, IReadOnlyList<NodeRunResponse> Nodes);
public sealed record MockupMetadataRequest(long ExpectedRevision, string Role, string ArtworkGroupKey, string? VariantKey, MockupRegions Regions);
public sealed record MockupReviewRequest(long ExpectedRevision, bool Approved);
public sealed record MockupResponse(Guid Id, Guid ProductId, string SourceType, string Role, string? ArtworkGroupKey, string? VariantKey, long Revision, string ApprovalStatus, long? ApprovedRevision, int Width, int Height, MockupRegions Regions, string PreviewUrl, IReadOnlyList<string> Warnings);
public sealed record AiStudioFrameRequest(Guid SourceMockupId, string Layout, string OutputFormat, bool Regenerate = false,
    string? ExpectedFingerprint = null, decimal? ConfirmedEstimatedCostUsd = null, string? CustomPrompt = null);
public sealed record AiStudioFrameQuote(string Fingerprint, int PromptVersion, decimal EstimatedCostUsd,
    MockupResponse? ExistingFrame, string Note);
public sealed record AiStudioFrameGenerationResponse(MockupResponse Frame, bool Reused, decimal EstimatedCostUsd);
public sealed record ScenePlan(Guid MockupId, long SourceRevision, string SourceHash, string Role, int SceneOrder, int DurationFrames, Region Crop, Region EndCrop, string Motion, string Transition, string Text, string GenerationStrategy, IReadOnlyList<string> Warnings,
    string? AiTemplateCode = null, int? PromptVersion = null, string? CompiledPrompt = null, AiVideoSceneOptions? AiSettings = null, Region? SourceCrop = null,
    string? AiProviderKey = null, string? AiModelKey = null, string? AiModelName = null);
public sealed record AiShotPlan(int SceneOrder, Guid MockupId, string SourceHash, string Model, string Preset, string Prompt, int DurationSeconds, string Fingerprint,
    string ProviderKey = AiVideoModel.ProviderKey, string ModelKey = AiVideoModel.ModelKey, int PromptVersion = 1, Region? SourceCrop = null);
public sealed record Storyboard(string Template, int TemplateVersion, int DurationSeconds, string ProductType, IReadOnlyList<ScenePlan> Scenes, string Fingerprint,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 1920, string AspectRatio = "9:16",
    string Mode = "standard", int TransitionOverlapFrames = 0, IReadOnlyList<AiShotPlan>? AiShots = null);
public sealed record VideoResponse(Guid Id, Guid RunId, string Mode, string Template, int Version, long ReviewRevision, string Status, string ApprovalStatus, string? PreviewUrl, string? ThumbnailUrl, JsonElement Qa, IReadOnlyList<ScenePlan> Scenes,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 1920, string AspectRatio = "9:16");
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
public sealed record ExportJobPayload(Guid ExportId, Guid VideoId, string VideoStorageKey, string VideoStorageVersion, string ThumbnailStorageKey, string ThumbnailStorageVersion, JsonElement Manifest);

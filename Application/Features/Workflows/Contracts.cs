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
public sealed record VideoOutputSpec(string Format, int Width, int Height, string AspectRatio);
public static class VideoOutputFormats
{
    public const string Default = "tall";
    public static bool IsSupported(string format) => format is "square" or "portrait" or "tall" or "landscape";
    public static VideoOutputSpec Resolve(string? format) => format switch
    {
        "square" => new("square", 1080, 1080, "1:1"),
        "portrait" => new("portrait", 1080, 1920, "9:16"),
        "landscape" => new("landscape", 1920, 1080, "16:9"),
        _ => new(Default, 1080, 2160, "1:2"),
    };
}
public sealed record AiBackgroundOptions(string Preset, string? Prompt, IReadOnlyList<int> SceneIndices);
public sealed record AiShotOptions(int SceneIndex, Guid MockupId, string CameraPreset, string? Prompt, int MaximumAttempts = 2);
public sealed record GenerateVideoConfig(
    string Mode = "standard", string Target = "etsy", string Template = "auto", int DurationSeconds = 12,
    string AssetSelection = "automatic", IReadOnlyList<Guid>? SelectedMockupIds = null,
    IReadOnlyList<Guid>? SceneOrder = null, string TextOverlay = "", StandardOptions? StandardOptions = null,
    AiBackgroundOptions? AiBackgroundOptions = null, AiShotOptions? AiShotOptions = null, bool FallbackToStandard = true,
    IReadOnlyList<string?>? SceneMotionPresets = null, int? TemplateVersion = null, string OutputFormat = VideoOutputFormats.Default);
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
    GenerateVideoConfig? Config = null, string? ExpectedStoryboardFingerprint = null);
public sealed record NodeRunResponse(Guid Id, string NodeId, string NodeType, string Status, int Attempt, string? Stage, int Progress, string? ErrorMessage, JsonElement Output);
public sealed record RunResponse(Guid Id, Guid WorkflowId, Guid ProductId, string Status, long Revision, long WorkflowRevision, DateTimeOffset CreatedAt, string? ErrorMessage, IReadOnlyList<NodeRunResponse> Nodes);
public sealed record MockupMetadataRequest(long ExpectedRevision, string Role, string ArtworkGroupKey, string? VariantKey, MockupRegions Regions);
public sealed record MockupReviewRequest(long ExpectedRevision, bool Approved);
public sealed record MockupResponse(Guid Id, Guid ProductId, string SourceType, string Role, string? ArtworkGroupKey, string? VariantKey, long Revision, string ApprovalStatus, long? ApprovedRevision, int Width, int Height, MockupRegions Regions, string PreviewUrl, IReadOnlyList<string> Warnings);
public sealed record ScenePlan(Guid MockupId, long SourceRevision, string SourceHash, string Role, int SceneOrder, int DurationFrames, Region Crop, Region EndCrop, string Motion, string Transition, string Text, string GenerationStrategy, IReadOnlyList<string> Warnings);
public sealed record Storyboard(string Template, int TemplateVersion, int DurationSeconds, string ProductType, IReadOnlyList<ScenePlan> Scenes, string Fingerprint,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 2160, string AspectRatio = "1:2");
public sealed record VideoResponse(Guid Id, Guid RunId, string Mode, string Template, int Version, long ReviewRevision, string Status, string ApprovalStatus, string? PreviewUrl, string? ThumbnailUrl, JsonElement Qa, IReadOnlyList<ScenePlan> Scenes,
    string OutputFormat = VideoOutputFormats.Default, int Width = 1080, int Height = 2160, string AspectRatio = "1:2");
public sealed record WorkerAsset(Guid MockupId, string Url, string FileName);
public sealed record UploadGrant(string Url, IReadOnlyDictionary<string, string> Fields, string StorageKey, string ResourceType);
public sealed record WorkerJob(Guid Id, Guid LeaseToken, string Kind, JsonElement Payload, IReadOnlyList<WorkerAsset> Assets, IReadOnlyDictionary<string, UploadGrant> Uploads);
public sealed record JobHeartbeat(Guid LeaseToken, string Stage, int Progress);
public sealed record MediaQa(string Codec, string PixelFormat, int Width, int Height, double Fps, double DurationSeconds, long Bytes, bool HasAudio, bool FullDecode);
public sealed record JobCompletion(Guid LeaseToken, string StorageKey, string StorageVersion, string? ThumbnailStorageKey, string? ThumbnailStorageVersion, MediaQa? Qa);
public sealed record JobFailure(Guid LeaseToken, string Message);
public sealed record RenderJobPayload(Guid VideoId, Storyboard Storyboard);
public sealed record ExportJobPayload(Guid ExportId, Guid VideoId, string VideoStorageKey, string VideoStorageVersion, string ThumbnailStorageKey, string ThumbnailStorageVersion, JsonElement Manifest);

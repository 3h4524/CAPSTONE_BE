using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// Bound to <c>Video:DeferredRender</c>. On (the default), Generate Video finishes as soon as the video's scenes can be shown, and the
/// final video is rendered later, in the background, only when a step needs the file: Publish to Etsy with a video, or
/// Export ZIP with its "final video" option. Set it to <c>false</c> to roll back: the video is then rendered inside Generate Video,
/// as it always was, and videos already waiting for their render are still finished.
/// </summary>
public sealed class DeferredRenderSettings
{
    public const string SectionName = "Video:DeferredRender";

    public bool Enabled { get; set; } = true;
}

/// <summary>The statuses and wording of a video whose file is made after it was generated.</summary>
public static class DeferredRender
{
    /// <summary>A render job whose clips are all there: it waits until a step asks for the final video. The media worker never sees it.</summary>
    public const string JobStatus = "awaiting_render";
    /// <summary>A video whose scenes are all available but that has no file yet: the seller previews and edits it in the browser.</summary>
    public const string VideoReady = "ready";
    public const string VideoRendering = "rendering";
    public const string VideoCompleted = "completed";
    /// <summary>What a step shows while it waits for the final video.</summary>
    public const string WaitStage = "Rendering video";

    /// <summary>True for a video that has no file, whichever way it is being made.</summary>
    public static bool HasNoFile(PromoVideo video) => video.Status is VideoReady or VideoRendering or "queued" or "pending";

    /// <summary>A video the seller can decide on: its file is made, or (deferred) its scenes are ready to be shown.</summary>
    public static bool CanBeReviewed(PromoVideo video) => video.Status is VideoCompleted or VideoReady or VideoRendering;

    /// <summary>A step is waiting for the final video when it was put there by <see cref="AwaitingRenderState"/>.</summary>
    public static bool IsWaiting(WorkflowNodeRun node)
    {
        if (node.Status != "running" || string.IsNullOrWhiteSpace(node.OutputSnapshot)) return false;
        try { return WorkflowJson.Read<AwaitingRenderState>(node.OutputSnapshot).AwaitingRender; }
        catch (JsonException) { return false; }
    }
}

/// <summary>Kept as the output snapshot of Export ZIP or Publish to Etsy while it waits for the final video to be rendered.</summary>
public sealed record AwaitingRenderState(bool AwaitingRender, Guid VideoId);

/// <summary>Which steps of a run need the final video file, read from the settings each step was started with.</summary>
public static class VideoRenderDemand
{
    /// <summary>Whether Export ZIP puts the final video in the package. A ZIP without it still has the video of every scene.</summary>
    public static bool ExportNeedsFinalVideo(WorkflowNodeRun? export) => export is not null && Flag(export.InputSnapshot, "includeFinalVideo");

    /// <summary>Publish to Etsy attaches the video unless the seller turned that off.</summary>
    public static bool EtsyNeedsVideo(WorkflowNodeRun? etsy) => etsy is not null && Flag(etsy.InputSnapshot, "includeVideo");

    public static bool Requires(IEnumerable<WorkflowNodeRun> nodes)
    {
        var all = nodes as IReadOnlyCollection<WorkflowNodeRun> ?? nodes.ToArray();
        return ExportNeedsFinalVideo(all.SingleOrDefault(x => x.NodeType == "export-zip"))
            || EtsyNeedsVideo(all.SingleOrDefault(x => x.NodeType == "publish-etsy"));
    }

    /// <summary>A switch that was never set is on: both steps always had the video.</summary>
    private static bool Flag(string? snapshot, string name)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return true;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            return document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(name, out var value)
                || value.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}

/// <summary>Which runs may have a media job worked on, kept in one place for the lease, the heartbeat and the recovery.</summary>
public static class MediaJobRunRules
{
    /// <summary>
    /// A render or export job runs while its run runs. With the final render deferred, a render may also run while the run
    /// waits for the seller (a batch is decided on video by video, then continued), because it was asked for when its video was approved.
    /// </summary>
    public static bool Works(string jobKind, string runStatus) =>
        runStatus == "running" || jobKind == "render" && runStatus is "waiting_for_review" or "queued";

    /// <summary>The statuses of a media job that is not finished: it still waits for something, or is being worked on.</summary>
    public static readonly string[] OpenJobStatuses = ["queued", "generating_clips", DeferredRender.JobStatus, "leased"];
}

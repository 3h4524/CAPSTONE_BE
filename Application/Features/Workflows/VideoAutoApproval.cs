using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>The settings of the Review Video step.</summary>
/// <param name="Mode">"manual" waits for the seller; "auto" approves the finished video and goes on to export.</param>
public sealed record ReviewVideoConfig(string Mode = "manual");

/// <summary>
/// Review Video set to "auto": the finished video is approved by the run's owner and the run goes on to export without
/// stopping, as Mockup Approval does for mock-ups in its own auto mode.
/// </summary>
/// <remarks>
/// Only a video that can be listed is approved: one whose file did not decode in full, or that fails one of Etsy's
/// required checks (duration, size, format, resolution), waits for the seller as in manual mode. Etsy's warnings
/// (an aspect ratio Etsy may crop, a resolution under the recommended one) do not stop it.
/// </remarks>
public static class VideoAutoApproval
{
    public const string AutoMode = "auto";

    public static bool IsAuto(WorkflowDefinition definition) =>
        definition.Nodes.SingleOrDefault(node => node.Type == "review-video") is { } review &&
        string.Equals(WorkflowJson.Config<ReviewVideoConfig>(review).Mode, AutoMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Approves <paramref name="video"/> when it is finished (or, with the final render deferred, when its scenes are ready and its
    /// plan can be listed), still undecided and listable; false leaves it as it is.
    /// </summary>
    public static bool Approve(PromoVideo video, Guid userId, DateTime now)
    {
        if (video.ApprovalStatus != "pending") return false;
        var passes = video.Status switch
        {
            DeferredRender.VideoCompleted => PassesRequiredChecks(video.QaResult),
            DeferredRender.VideoReady => PlannedPassesRequiredChecks(video.ConfigSnapshot),
            _ => false
        };
        if (!passes) return false;
        video.ApprovalStatus = "approved";
        video.ReviewRevision++;
        video.ApprovedBy = userId;
        video.ApprovedAt = now;
        return true;
    }

    /// <summary>The checks saved when the render finished: the file decoded in full and Etsy's result is not "fail".</summary>
    internal static bool PassesRequiredChecks(string? qaResult)
    {
        if (string.IsNullOrWhiteSpace(qaResult)) return false;
        try
        {
            var saved = WorkflowJson.Read<SavedQa>(qaResult);
            return saved.Technical?.FullDecode == true && saved.EtsyCompliance is { } etsy && etsy.Status != "fail";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Etsy's required checks against the storyboard saved with the video, for a video that has no file yet.</summary>
    internal static bool PlannedPassesRequiredChecks(string? configSnapshot)
    {
        if (string.IsNullOrWhiteSpace(configSnapshot)) return false;
        try
        {
            using var document = JsonDocument.Parse(configSnapshot);
            if (!document.RootElement.TryGetProperty("storyboard", out var element)) return false;
            var board = element.Deserialize<Storyboard>(WorkflowJson.Options);
            return board is not null && EtsyVideoCompliance.EvaluatePlanned(board).Status != "fail";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record SavedQa(MediaQa? Technical, EtsyComplianceResult? EtsyCompliance);
}

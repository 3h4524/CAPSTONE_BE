using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>The settings of the Mockup Approval step.</summary>
/// <param name="Mode">"manual" waits for the seller; "auto" approves the mock-ups waiting for review and goes on.</param>
public sealed record ApprovalGateConfig(string Mode = "manual");

/// <summary>
/// Mockup Approval set to "auto": the mock-ups waiting for review are approved by the run's owner and the video goes
/// on without stopping, as Design approval does for designs in its own auto mode.
/// </summary>
/// <remarks>
/// Only what the seller has not decided is approved: a rejected mock-up stays rejected, an AI keyframe is reviewed on
/// its own, and a composite replaced by a newer one of the same template and color (<see cref="MockupSources.Superseded"/>)
/// is left as it is. A paid AI scene still waits for its cost to be confirmed; that is not a review the mode replaces.
/// </remarks>
public static class MockupAutoApproval
{
    public const string AutoMode = "auto";

    public static bool IsAuto(WorkflowDefinition definition) =>
        definition.Nodes.SingleOrDefault(node => node.Type == "approval-gate") is { } gate &&
        string.Equals(WorkflowJson.Config<ApprovalGateConfig>(gate).Mode, AutoMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Approves the stored mock-ups among <paramref name="stored"/> that wait for review; returns how many.</summary>
    /// <param name="stored">Mock-ups stored as video sources, every one of them, so replaced ones are recognised.</param>
    public static int Approve(IReadOnlyCollection<MockupImage> stored, Guid userId, DateTime now)
    {
        var superseded = MockupSources.Superseded(stored);
        var waiting = stored.Where(row => row.DeletedAt == null && row.ContentHash != null && row.ApprovalStatus == "pending"
            && !superseded.Contains(row.Id) && AiStudioFrameIdentity.Read(row.Regions) is null).ToArray();
        foreach (var row in waiting)
        {
            row.ApprovalStatus = "approved";
            row.ApprovedRevision = row.MetadataRevision;
            row.ApprovedBy = userId;
            row.ApprovedAt = now;
        }
        return waiting.Length;
    }

    /// <summary>Whether a video can start without the seller: AI scenes are billed per clip and confirmed first.</summary>
    public static bool NeedsNoConsent(GenerateVideoConfig config) =>
        config.Mode != "ai_shot" && (config.AiScenes is null || config.AiScenes.All(scene => scene.Mode == "standard"));
}

using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>What a stopped media job says about itself, and whether trying it again could help.</summary>
/// <param name="Message">What the seller reads.</param>
/// <param name="RetryWontHelp">A refusal, a changed source or an uncertain submission: trying again cannot change it.</param>
/// <param name="Busy">Over quota or a temporary provider error, which billed nothing.</param>
public sealed record MediaJobFailure(string Message, bool RetryWontHelp, bool Busy);

/// <summary>
/// The wording of a failed media job, shared by the worker's failure report and by the API when it makes the AI scenes
/// itself. The worker only names what went wrong; what the seller reads is written here, with the provider's reason
/// when the scene has one.
/// </summary>
public static class MediaJobFailures
{
    public static MediaJobFailure Describe(string code, MediaJob job)
    {
        var reason = code is "AiShotRejected" or "AiShotFailed" ? AiShotFailure(job) : null;
        var note = code switch
        {
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" => AiShotFailure(job, "submitting"),
            "AiVideoBusy" => AiShotFailure(job, "pending"),
            _ => null
        };
        var message = code switch
        {
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" when note != null => $"AI video request status is uncertain: {note} Retry will not submit it again; choose Regenerate AI clips after checking provider usage.",
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" => "AI video request status is uncertain. Retry will not submit it again; choose Regenerate AI clips after checking provider usage.",
            "AiVideoBusy" => $"{note ?? "The AI video provider is busy or over quota."} Nothing was billed for it; choose Retry checkpoint once the quota allows it.",
            "SourceChanged" => "A source mock-up changed or is no longer approved. Approve the mock-ups again, then retry.",
            "SourceDownloadFailed" => "An export source could not be downloaded. Check that the approved design, mock-ups and video are still available, then retry.",
            "AiShotRejected" or "AiShotFailed" when reason != null =>
                $"The video provider did not generate this AI scene: {reason} Choose Retry checkpoint to submit it again; if it keeps refusing, change the source image or the motion direction.",
            "AiShotRejected" => "The video provider rejected this AI scene. Change its approved source or motion instruction, then regenerate AI clips.",
            "AiShotFailed" => "AI Shot could not finish. Review the approved source or prompt, then regenerate AI clips if needed.",
            _ => "Worker could not finish media processing. Retry or review source assets."
        };
        // Trying again by itself cannot change a refusal, a changed source or an uncertain submission.
        var retryWontHelp = code is "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" or "AiShotRejected" or "SourceChanged" || reason != null;
        return new(message, retryWontHelp, code == "AiVideoBusy");
    }

    /// <summary>What the AI scene in the given state recorded about itself: its provider's refusal, or why its outcome is unknown.</summary>
    public static string? AiShotFailure(MediaJob job, string status = "failed")
    {
        if (job.Kind != "render") return null;
        try
        {
            return WorkflowJson.Read<AiRenderJobPayload>(job.Payload).AiShotStates?
                .FirstOrDefault(x => x.Status == status && !string.IsNullOrWhiteSpace(x.Failure))?.Failure;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

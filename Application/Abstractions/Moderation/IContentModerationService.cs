namespace APCS.Application.Abstractions.Moderation;

/// <summary>
/// The platform's own content check (guardrail) for seller text sent to an image or video provider and for the images
/// a provider returns. It never throws for an unavailable check: the verdict says <see cref="ModerationDecision.Unchecked"/>
/// for an output, and lets an input through, as docs/CONTENT_MODERATION.md describes.
/// </summary>
public interface IContentModerationService
{
    /// <summary>
    /// Checks the untrusted text of one request together (one Gemini call at most). Empty fields are ignored, and a
    /// request with no text left is allowed without any check. <paramref name="blocklistOnly"/> skips the Gemini
    /// check, for a bulk import that would otherwise wait on hundreds of calls; the text is checked in full again before
    /// it reaches a provider.
    /// </summary>
    Task<ModerationVerdict> CheckTextAsync(
        IReadOnlyList<ModerationText> texts,
        ModerationContext context,
        CancellationToken cancellationToken = default,
        bool blocklistOnly = false);

    /// <summary>Checks one image a provider returned.</summary>
    Task<ModerationVerdict> CheckImageAsync(
        byte[] imageBytes,
        string mimeType,
        ModerationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The decision last recorded for an output image, or null when it was never checked (an image made before the
    /// guardrail, or while it was disabled).
    /// </summary>
    Task<ModerationDecision?> GetLatestOutputDecisionAsync(
        string subjectType,
        Guid subjectId,
        CancellationToken cancellationToken = default);
}

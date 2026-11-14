using APCS.Application.Abstractions.Moderation;
using APCS.Common.Models;

namespace APCS.Application.Features.Moderation;

/// <summary>
/// Approval of a generated image waits for its content check. An image whose check could not run when it was made
/// (<see cref="ModerationDecision.Unchecked"/>) is checked again at the moment it is approved; an image that was never
/// checked (made before the guardrail) is not held back.
/// </summary>
public static class ModerationApproval
{
    /// <summary>Null when the image may be approved, otherwise why not.</summary>
    /// <param name="moderation">The guardrail.</param>
    /// <param name="context">The image's output context; <see cref="ModerationContext.SubjectId"/> must be set.</param>
    /// <param name="readPng">Reads the stored image back as PNG bytes; null when it cannot be read here.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<Error?> EnsureApprovableAsync(
        this IContentModerationService moderation,
        ModerationContext context,
        Func<CancellationToken, Task<byte[]>>? readPng,
        CancellationToken cancellationToken)
    {
        if (context.SubjectId is not Guid subjectId) return null;
        var latest = await moderation.GetLatestOutputDecisionAsync(context.SubjectType, subjectId, cancellationToken);
        if (latest is null or ModerationDecision.Allow or ModerationDecision.Flagged) return null;
        if (latest == ModerationDecision.Block)
            return ModerationErrors.Blocked(new ModerationVerdict(ModerationDecision.Block, [], ModerationDeciders.Gemini));
        if (readPng is null) return ModerationErrors.Pending();

        byte[] image;
        try
        {
            image = await readPng(cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or IOException)
        {
            return ModerationErrors.Pending();
        }

        var verdict = await moderation.CheckImageAsync(image, "image/png", context, cancellationToken);
        if (verdict.IsBlocked) return ModerationErrors.Blocked(verdict);
        return verdict.Decision == ModerationDecision.Unchecked ? ModerationErrors.Pending() : null;
    }
}

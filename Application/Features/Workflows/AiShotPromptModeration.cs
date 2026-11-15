using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation;
using APCS.Common.Models;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// The AI scene prompts of a storyboard are checked whole, after the prompt rewrite: once rewritten, the seller's
/// words can no longer be told apart from the template's, and the rewrite itself could have changed their meaning.
/// </summary>
public static class AiShotPromptModeration
{
    public static async Task<Result<Storyboard>> CheckAiShotPromptsAsync(
        this IContentModerationService? moderation,
        Storyboard storyboard,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (moderation is null || storyboard.AiShots is not { Count: > 0 } shots) return Result.Success(storyboard);
        var verdict = await moderation.CheckTextAsync(
            shots.Select(shot => new ModerationText($"the scene {shot.SceneOrder + 1} video prompt", shot.Prompt)).ToList(),
            new ModerationContext(userId, ModerationFeatures.AiVideo, ModerationDirection.Input, ModerationSubjects.AiShot),
            cancellationToken);
        return verdict.IsBlocked ? Result.Failure<Storyboard>(ModerationErrors.Blocked(verdict)) : Result.Success(storyboard);
    }
}

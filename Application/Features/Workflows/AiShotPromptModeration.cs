using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation;
using APCS.Common.Models;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// The content check of AI video prompts looks only at what the seller wrote into the scenes, once, when that text is
/// stored: the workflow is saved, a design of a batch run gets settings of its own, or a run is rendered again with new
/// settings. Planning, quoting and rendering then use text that was already checked, so a run waits on no further check.
/// The template prompts, the product context and the preservation rules are the platform's own text and are not
/// checked, and neither is the finished video.
/// </summary>
public static class AiShotPromptModeration
{
    /// <summary>
    /// The seller's own text in one Generate Video config, named as the seller sees it. The names are read back by the
    /// workflow editor to mark the field the check refused ("scene 2 custom prompt").
    /// </summary>
    public static IReadOnlyList<ModerationText> SellerTexts(GenerateVideoConfig config)
    {
        var texts = new List<ModerationText> { new("the shared scene directions", config.AiGlobalInstructions) };
        foreach (var (scene, index) in (config.AiScenes ?? []).Select((scene, index) => (scene, index + 1)))
        {
            texts.Add(new($"scene {index} custom prompt", scene.CustomPrompt));
            texts.Add(new($"scene {index} instructions", scene.ExtraInstructions));
            texts.Add(new($"scene {index} start", scene.StartingState));
            texts.Add(new($"scene {index} action", scene.PrimaryAction));
            texts.Add(new($"scene {index} end", scene.EndingState));
            texts.Add(new($"scene {index} keyframe prompt", scene.StudioPrompt));
        }
        return texts.Where(text => !string.IsNullOrWhiteSpace(text.Value)).ToList();
    }

    /// <summary>The refusal for the seller's scene text of a config about to be stored; null when it may be kept.</summary>
    public static async Task<Error?> CheckSellerTextsAsync(
        this IContentModerationService? moderation,
        GenerateVideoConfig config,
        Guid userId,
        string subjectType,
        Guid? subjectId,
        CancellationToken cancellationToken)
    {
        if (moderation is null) return null;
        var texts = SellerTexts(config);
        if (texts.Count == 0) return null;
        var verdict = await moderation.CheckTextAsync(texts,
            new ModerationContext(userId, ModerationFeatures.AiVideo, ModerationDirection.Input, subjectType, subjectId),
            cancellationToken);
        return verdict.IsBlocked ? ModerationErrors.Blocked(verdict) : null;
    }
}

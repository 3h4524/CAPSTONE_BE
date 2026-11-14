using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.StyleArtPresets.Common;

public static class StyleArtPresetTranslator
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public sealed record InlineTranslationOutcome(bool Success, StyleArtPresetTranslations.ViTranslation? Vi);

    public static async Task<InlineTranslationOutcome> TranslateEnToViAsync(
        string name,
        string description,
        IReadOnlyList<string> recommendations,
        ITranslationService translationService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TryTranslateAsync(name, description, recommendations, translationService, logger, cancellationToken);
        if (outcome.Success)
            return outcome;

        await Task.Delay(RetryDelay, cancellationToken);
        return await TryTranslateAsync(name, description, recommendations, translationService, logger, cancellationToken);
    }

    private static async Task<InlineTranslationOutcome> TryTranslateAsync(
        string name,
        string description,
        IReadOnlyList<string> recommendations,
        ITranslationService translationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AttemptTimeout);

            var items = new List<TranslateTextItemDto>
            {
                new("name", name),
                new("description", description)
            };
            for (var index = 0; index < recommendations.Count; index++)
            {
                items.Add(new TranslateTextItemDto($"recommendations/{index}", recommendations[index]));
            }

            var result = await translationService.TranslateAsync(
                new TranslateTextsRequestDto("en", "vi", items),
                timeout.Token);

            if (!result.IsSuccess)
                return new InlineTranslationOutcome(false, null);

            var byKey = result.Value.Items.ToDictionary(item => item.Key, StringComparer.Ordinal);
            if (!byKey.Values.Any(item => item.IsTranslated))
            {
                logger.LogWarning("Google translation returned no translated items; marking the row for retry.");
                return new InlineTranslationOutcome(false, null);
            }

            var translatedRecommendations = Enumerable.Range(0, recommendations.Count)
                .Select(index => byKey.TryGetValue($"recommendations/{index}", out var item)
                    ? item.Text
                    : recommendations[index])
                .ToArray();

            return new InlineTranslationOutcome(
                true,
                new StyleArtPresetTranslations.ViTranslation(
                    byKey.TryGetValue("name", out var translatedName) ? translatedName.Text : name,
                    byKey.TryGetValue("description", out var translatedDescription) ? translatedDescription.Text : description,
                    translatedRecommendations));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Google translation attempt timed out; marking the row for retry.");
            return new InlineTranslationOutcome(false, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Google translation attempt failed; marking the row for retry.");
            return new InlineTranslationOutcome(false, null);
        }
    }
}

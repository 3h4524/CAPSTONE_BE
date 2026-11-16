using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.Translations;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class StyleArtPresetTranslationWorker(
    IServiceProvider serviceProvider,
    StyleArtPresetTranslationSignal signal,
    TimeProvider timeProvider,
    ILogger<StyleArtPresetTranslationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleSafetyCheck = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ShortestWait = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await QueueUntranslatedAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var seen = signal.Saved.Version;
                var nextDueAt = await TranslateDueAsync(stoppingToken);
                await signal.Saved.WaitAsync(seen, WaitUntil(nextDueAt), stoppingToken, timeProvider);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task QueueUntranslatedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var queued = StyleArtPresetTranslations.Queued(now);

            var count = await db.StyleArtPresets
                .Where(preset => preset.UserId == null && preset.IsActive && preset.Translations == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(preset => preset.Translations, queued)
                        .SetProperty(preset => preset.UpdatedAt, now),
                    cancellationToken);

            if (count > 0)
                logger.LogInformation("Queued {Count} system style preset(s) for translation.", count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not queue untranslated system style presets.");
        }
    }

    private async Task<DateTime?> TranslateDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var translationService = scope.ServiceProvider.GetRequiredService<ITranslationService>();

            var rows = await db.StyleArtPresets
                .AsNoTracking()
                .Where(preset => preset.UserId == null && preset.IsActive && preset.Translations != null)
                .Select(preset => new { preset.Id, preset.Name, preset.Description, preset.Recommendations, preset.Translations, preset.UpdatedAt })
                .ToListAsync(cancellationToken);

            DateTime? nextDueAt = null;
            var translatedAny = false;

            foreach (var row in rows)
            {
                var state = StyleArtPresetTranslations.ParseState(row.Translations);
                if (state is not { Status: StyleArtPresetTranslations.Pending })
                    continue;

                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (!StyleArtPresetTranslations.IsDue(state, now))
                {
                    nextDueAt = Earliest(nextDueAt, state.NextRetryAt);
                    continue;
                }

                StyleArtPresetRules.TryParseRecommendations(row.Recommendations, out var recommendations);
                var outcome = await StyleArtPresetTranslator.TranslateEnToViAsync(
                    row.Name, row.Description, recommendations, translationService, logger, cancellationToken);

                var translations = outcome is { Success: true, Vi: { } vi }
                    ? StyleArtPresetTranslations.Succeeded(vi)
                    : StyleArtPresetTranslations.AfterFailedAttempt(state, now);

                var saved = await db.StyleArtPresets
                    .Where(preset => preset.Id == row.Id && preset.UpdatedAt == row.UpdatedAt)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(preset => preset.Translations, translations)
                            .SetProperty(preset => preset.UpdatedAt, now),
                        cancellationToken);

                if (saved == 0)
                {
                    nextDueAt = Earliest(nextDueAt, now);
                    continue;
                }

                if (outcome.Success)
                {
                    translatedAny = true;
                    continue;
                }

                var next = StyleArtPresetTranslations.ParseState(translations);
                logger.LogWarning(
                    "Translation failed for system style {Name} (attempt {Attempts}, now {Status}).",
                    row.Name, next?.Attempts, next?.Status);
                nextDueAt = Earliest(nextDueAt, next?.NextRetryAt);
            }

            if (translatedAny)
                await cache.RemoveAsync(StyleArtPresetCacheKeys.SystemRows, cancellationToken);

            return nextDueAt;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not translate pending system style presets.");
            return timeProvider.GetUtcNow().UtcDateTime + RetryAfterError;
        }
    }

    private TimeSpan WaitUntil(DateTime? dueAt)
    {
        if (dueAt is not { } due)
            return IdleSafetyCheck;

        var wait = due - timeProvider.GetUtcNow().UtcDateTime;
        if (wait < ShortestWait)
            return ShortestWait;
        return wait > IdleSafetyCheck ? IdleSafetyCheck : wait;
    }

    private static DateTime? Earliest(DateTime? current, DateTime? candidate) =>
        candidate is null ? current
        : current is null || candidate < current ? candidate
        : current;
}

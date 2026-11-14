using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.Translations;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class TranslationRetrySweeper(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<TranslationRetrySweeper> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
    private const int MaximumAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BackfillAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckEvery, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.StyleArtPresets
                .Where(preset => preset.UserId == null && preset.IsActive && preset.Translations == null)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                row.Translations = StyleArtPresetTranslations.SerializeDocument(
                    null, manual: false, status: "pending", attempts: 0,
                    nextRetryAt: timeProvider.GetUtcNow().UtcDateTime);
                row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            }

            if (rows.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Queued {Count} system style preset(s) for translation backfill.", rows.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not queue style presets for translation backfill.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var translationService = scope.ServiceProvider.GetRequiredService<ITranslationService>();
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var candidates = await db.StyleArtPresets
                .Where(preset => preset.UserId == null && preset.IsActive && preset.Translations != null)
                .ToListAsync(cancellationToken);

            var touched = false;
            foreach (var row in candidates)
            {
                var state = StyleArtPresetTranslations.ParseState(row.Translations);
                if (state is null
                    || state.Manual
                    || !string.Equals(state.Status, "pending", StringComparison.Ordinal)
                    || state.NextRetryAt is null
                    || state.NextRetryAt > now)
                {
                    continue;
                }

                StyleArtPresetRules.TryParseRecommendations(row.Recommendations, out var recommendations);
                var outcome = await StyleArtPresetTranslator.TranslateEnToViAsync(
                    row.Name, row.Description, recommendations, translationService, logger, cancellationToken);

                if (outcome.Success)
                {
                    row.Translations = StyleArtPresetTranslations.SerializeDocument(
                        outcome.Vi, manual: false, status: "ok", attempts: 0, nextRetryAt: null);
                    row.UpdatedAt = now;
                    await cache.RemoveAsync(StyleArtPresetCacheKeys.SystemRows, cancellationToken);
                }
                else
                {
                    var attempts = state.Attempts + 1;
                    row.Translations = attempts >= MaximumAttempts
                        ? StyleArtPresetTranslations.SerializeDocument(
                            StyleArtPresetTranslations.ParseViDocument(row.Translations),
                            manual: false, status: "failed", attempts, nextRetryAt: null)
                        : StyleArtPresetTranslations.SerializeDocument(
                            StyleArtPresetTranslations.ParseViDocument(row.Translations),
                            manual: false, status: "pending", attempts,
                            nextRetryAt: now.AddMinutes(attempts == 2 ? 10 : 5));
                    row.UpdatedAt = now;
                    logger.LogWarning("Translation retry failed for system style {Name} (attempt {Attempts}).", row.Name, attempts);
                }

                touched = true;
            }

            if (touched)
                await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not process pending style preset translations.");
        }
    }
}

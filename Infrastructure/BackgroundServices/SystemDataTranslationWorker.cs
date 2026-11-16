using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Translation;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.SystemTranslations;
using APCS.Application.Features.Translations;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Keeps the Vietnamese translation of the system catalog current. A row whose English text no longer matches the text
/// its translation was made from is translated again, so edits from the Admin Portal or a migration need no signal.
/// Without a configured provider nothing is written, so a local API without a key cannot mark rows as failed.
/// </summary>
public sealed class SystemDataTranslationWorker(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<SystemDataTranslationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepAsync(stoppingToken);
                await Task.Delay(SweepInterval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            if (!services.GetRequiredService<ITranslationProvider>().IsConfigured)
                return;

            var db = services.GetRequiredService<AppDbContext>();
            var translator = services.GetRequiredService<ITranslationService>();

            if (await TranslateAsync(db, "design_templates",
                    db.DesignTemplates.Where(template => template.IsSystemTemplate == true && template.DeletedAt == null),
                    SystemTranslationFields.For, template => template.Translations,
                    (template, value) => template.Translations = value, translator, cancellationToken) > 0)
            {
                await services.GetRequiredService<ICacheService>()
                    .RemoveAsync(DesignTemplateCacheKeys.SystemCatalog, cancellationToken);
            }

            await TranslateAsync(db, "video_templates",
                db.VideoTemplates.Where(template => template.IsSystemTemplate == true),
                SystemTranslationFields.For, template => template.Translations,
                (template, value) => template.Translations = value, translator, cancellationToken);

            await TranslateAsync(db, "ai_character_models",
                db.AiCharacterModels.Where(model => model.DeletedAt == null),
                SystemTranslationFields.For, model => model.Translations,
                (model, value) => model.Translations = value, translator, cancellationToken);

            await TranslateAsync(db, "music_tracks",
                db.MusicTracks.Where(track => track.DeletedAt == null),
                SystemTranslationFields.For, track => track.Translations,
                (track, value) => track.Translations = value, translator, cancellationToken);

            await TranslateAsync(db, "subscription_plans",
                db.SubscriptionPlans,
                SystemTranslationFields.For, plan => plan.Translations,
                (plan, value) => plan.Translations = value, translator, cancellationToken);

            if (await TranslateAsync(db, "features",
                    db.Features,
                    SystemTranslationFields.For, feature => feature.Translations,
                    (feature, value) => feature.Translations = value, translator, cancellationToken) > 0)
            {
                services.GetRequiredService<IFeatureCatalog>().Invalidate();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not translate the system catalog.");
        }
    }

    private async Task<int> TranslateAsync<TEntity>(
        AppDbContext db,
        string table,
        IQueryable<TEntity> rows,
        Func<TEntity, IReadOnlyDictionary<string, string>> fieldsOf,
        Func<TEntity, string?> translationsOf,
        Action<TEntity, string> saveTranslations,
        ITranslationService translator,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var translated = 0;
        foreach (var row in await rows.ToListAsync(cancellationToken))
        {
            var fields = fieldsOf(row);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (!SystemTranslationDocument.NeedsTranslation(translationsOf(row), fields, now))
                continue;

            IReadOnlyDictionary<string, string>? vi = null;
            try
            {
                vi = await SystemTextTranslator.TranslateToVietnameseAsync(fields, translator, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Translation request failed for a {Table} row.", table);
            }

            saveTranslations(row, vi is null
                ? SystemTranslationDocument.AfterFailedAttempt(translationsOf(row), fields, now)
                : SystemTranslationDocument.Succeeded(fields, vi));
            await db.SaveChangesAsync(cancellationToken);

            if (vi is null)
                logger.LogWarning("Could not translate a {Table} row; it will be retried.", table);
            else
                translated++;
        }

        if (translated > 0)
            logger.LogInformation("Translated {Count} {Table} row(s) into Vietnamese.", translated, table);
        return translated;
    }
}

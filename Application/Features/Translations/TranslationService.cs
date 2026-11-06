using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Translation;
using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Translations;

/// <summary>
/// Resolves translations from Redis, PostgreSQL, and the configured provider in that order.
/// </summary>
public sealed class TranslationService(
    IRepository<TranslationEntry> entries,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    ITranslationProvider provider,
    IValidator<TranslateTextsRequestDto> validator) : ITranslationService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    public async Task<Result<TranslateTextsResponseDto>> TranslateAsync(
        TranslateTextsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<TranslateTextsResponseDto>(Error.Validation(validation.ToString()));

        var sourceLocale = request.SourceLocale.Trim().ToLowerInvariant();
        var targetLocale = request.TargetLocale.Trim().ToLowerInvariant();
        var distinctItems = request.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => new TranslationWorkItem(item.Text.Trim(), Hash(item.Text.Trim(), sourceLocale)))
            .GroupBy(item => item.SourceHash, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        if (sourceLocale == targetLocale || distinctItems.Length == 0)
        {
            return Result.Success(new TranslateTextsResponseDto(
                request.Items.Select(item => new TranslatedTextDto(item.Key, item.Text, false, false)).ToArray()));
        }

        var providerName = provider.Name;
        var hashes = distinctItems.Select(item => item.SourceHash).ToArray();
        var resolved = new Dictionary<string, TranslationEntry>(StringComparer.Ordinal);

        foreach (var item in distinctItems)
        {
            var cacheKey = CacheKey(providerName, sourceLocale, targetLocale, item.SourceHash);
            var cached = await cache.GetAsync<TranslationEntry>(cacheKey, cancellationToken);
            if (cached is not null)
                resolved[item.SourceHash] = cached;
        }

        var missingHashes = hashes.Where(hash => !resolved.ContainsKey(hash)).ToArray();
        if (missingHashes.Length > 0)
        {
            var databaseEntries = await entries.Query()
                .Where(entry => missingHashes.Contains(entry.SourceHash) &&
                                entry.SourceLocale == sourceLocale &&
                                entry.TargetLocale == targetLocale &&
                                entry.Provider == providerName)
                .ToListAsync(cancellationToken);

            foreach (var entry in databaseEntries)
            {
                resolved[entry.SourceHash] = entry;
                await cache.SetAsync(CacheKey(providerName, sourceLocale, targetLocale, entry.SourceHash), entry, CacheDuration, cancellationToken);
            }
        }

        var missing = distinctItems
            .Where(item => !resolved.ContainsKey(item.SourceHash))
            .ToArray();
        var freshlyTranslated = new HashSet<string>(StringComparer.Ordinal);

        if (missing.Length > 0 && provider.IsConfigured)
        {
            var translated = await provider.TranslateAsync(
                missing.Select(item => new TranslationInput(item.SourceHash, item.SourceText)).ToArray(),
                sourceLocale,
                targetLocale,
                cancellationToken);

            foreach (var item in missing)
            {
                if (!translated.TryGetValue(item.SourceHash, out var translatedText) || string.IsNullOrWhiteSpace(translatedText))
                    continue;

                var entry = new TranslationEntry
                {
                    Id = Guid.NewGuid(),
                    SourceHash = item.SourceHash,
                    SourceText = item.SourceText,
                    SourceLocale = sourceLocale,
                    TargetLocale = targetLocale,
                    TranslatedText = translatedText,
                    Provider = providerName,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await entries.AddAsync(entry, cancellationToken: cancellationToken);
                resolved[item.SourceHash] = entry;
                freshlyTranslated.Add(item.SourceHash);
                await cache.SetAsync(CacheKey(providerName, sourceLocale, targetLocale, item.SourceHash), entry, CacheDuration, cancellationToken);
            }

            if (freshlyTranslated.Count > 0)
                await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var result = request.Items.Select(item =>
        {
            var sourceText = item.Text.Trim();
            var sourceHash = Hash(sourceText, sourceLocale);
            var found = resolved.TryGetValue(sourceHash, out var entry);
            var output = found ? entry!.TranslatedText : sourceText;
            return new TranslatedTextDto(item.Key, output, found, found && !freshlyTranslated.Contains(sourceHash));
        }).ToArray();

        return Result.Success(new TranslateTextsResponseDto(result));
    }

    private static string Hash(string text, string sourceLocale)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceLocale}\n{text}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string CacheKey(string provider, string sourceLocale, string targetLocale, string hash) =>
        $"translation:{provider}:{sourceLocale}:{targetLocale}:{hash}";

    private sealed record TranslationWorkItem(string SourceText, string SourceHash);
}

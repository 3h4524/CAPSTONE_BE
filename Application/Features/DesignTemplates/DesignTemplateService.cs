using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.DesignTemplates;

/// <summary>Coordinates the Seller design-template library.</summary>
public sealed class DesignTemplateService(
    IDesignTemplateRepository repository,
    ICacheService cache,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<ListDesignTemplatesRequestDto> listValidator,
    IValidator<CreateDesignTemplateRequestDto> createValidator,
    IValidator<UpdateDesignTemplateRequestDto> updateValidator,
    ILogger<DesignTemplateService> logger) : IDesignTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<PagedResult<DesignTemplateSummaryResponseDto>>> ListAsync(
        ListDesignTemplatesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await listValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<PagedResult<DesignTemplateSummaryResponseDto>>(
                validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<PagedResult<DesignTemplateSummaryResponseDto>>(
                DesignTemplateErrors.Unauthenticated());
        }

        IEnumerable<DesignTemplateCacheItem> query = request.Scope switch
        {
            DesignTemplateScopes.System => await GetSystemCatalogAsync(cancellationToken),
            DesignTemplateScopes.Personal => await GetPersonalCatalogAsync(userId.Value, cancellationToken),
            _ => (await GetSystemCatalogAsync(cancellationToken))
                .Concat(await GetPersonalCatalogAsync(userId.Value, cancellationToken))
        };

        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(template =>
                template.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.NicheCategory))
        {
            query = query.Where(template =>
                string.Equals(template.NicheCategory, request.NicheCategory, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(request.ArtStyle))
        {
            query = query.Where(template =>
                string.Equals(template.ArtStyle, request.ArtStyle, StringComparison.Ordinal));
        }

        var filtered = query
            .OrderByDescending(template => template.CreatedAtUtc)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var page = filtered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(MapSummary)
            .ToArray();

        return Result.Success(new PagedResult<DesignTemplateSummaryResponseDto>(
            page,
            filtered.Length,
            request.PageNumber,
            request.PageSize));
    }

    public async Task<Result<DesignTemplateDetailResponseDto>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.Unauthenticated());
        }

        var template = await repository.GetVisibleAsync(id, userId.Value, cancellationToken);

        return template is null
            ? Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.NotFound())
            : Result.Success(MapDetail(MapCacheItem(template)));
    }

    public Task<Result<DesignTemplateOptionsResponseDto>> GetOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = new DesignTemplateOptionsResponseDto(
            DesignTemplateNiches.All
                .Select(niche => new DesignTemplateOptionResponseDto(niche, niche))
                .ToArray(),
            DesignTemplateArtStyles.Labels
                .Select(style => new DesignTemplateOptionResponseDto(style.Key, style.Value))
                .ToArray(),
            DesignTemplatePlaceholders.All.Select(placeholder => $"{{{placeholder}}}").ToArray(),
            DesignTemplateRules.MaximumBasePromptLength,
            DesignTemplateRules.MaximumExamples,
            PreviewGenerationEnabled: false);

        return Task.FromResult(Result.Success(response));
    }

    public async Task<Result<DesignTemplateDetailResponseDto>> CreateAsync(
        CreateDesignTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.Unauthenticated());
        }

        var name = NormalizeText(request.Name);
        if (await repository.PersonalNameExistsAsync(
                userId.Value,
                NormalizeNameForLookup(name),
                cancellationToken: cancellationToken))
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.DuplicateName());
        }

        var now = timeProvider.GetUtcNow();
        var template = new DesignTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Name = name,
            Type = DesignTemplateTypes.Personal,
            NicheCategory = request.NicheCategory,
            ArtStyle = request.ArtStyle,
            BasePrompt = request.BasePrompt.Trim(),
            NegativePrompt = NormalizeOptionalText(request.NegativePrompt),
            ExamplePrompts = SerializeExamples(request.Examples),
            StyleDescription = BuildPersonalDescription(request.ArtStyle, request.NicheCategory),
            IsSystemTemplate = false,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };

        if (!await repository.TryAddAsync(template, cancellationToken))
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.DuplicateName());
        }

        await InvalidatePersonalCacheAsync(userId.Value);
        return Result.Success(MapDetail(MapCacheItem(template)));
    }

    public async Task<Result<DesignTemplateDetailResponseDto>> UpdateAsync(
        Guid id,
        UpdateDesignTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(validation.ToValidationError());
        }

        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.Unauthenticated());
        }

        var template = await repository.GetForUpdateAsync(id, cancellationToken);
        if (template is null || template.DeletedAt is not null || template.IsActive != true)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.NotFound());
        }

        if (template.IsSystemTemplate == true)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(
                DesignTemplateErrors.SystemTemplateReadOnly());
        }

        if (template.UserId != userId)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.NotFound());
        }

        var name = NormalizeText(request.Name);
        if (await repository.PersonalNameExistsAsync(
                userId.Value,
                NormalizeNameForLookup(name),
                template.Id,
                cancellationToken))
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.DuplicateName());
        }

        template.Name = name;
        template.NicheCategory = request.NicheCategory;
        template.ArtStyle = request.ArtStyle;
        template.BasePrompt = request.BasePrompt.Trim();
        template.NegativePrompt = NormalizeOptionalText(request.NegativePrompt);
        template.ExamplePrompts = SerializeExamples(request.Examples);
        template.StyleDescription = BuildPersonalDescription(request.ArtStyle, request.NicheCategory);
        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.DuplicateName());
        }

        await InvalidatePersonalCacheAsync(userId.Value);
        return Result.Success(MapDetail(MapCacheItem(template)));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure(DesignTemplateErrors.Unauthenticated());
        }

        var template = await repository.GetForUpdateAsync(id, cancellationToken);
        if (template is null || template.DeletedAt is not null || template.IsActive != true)
        {
            return Result.Failure(DesignTemplateErrors.NotFound());
        }

        if (template.IsSystemTemplate == true)
        {
            return Result.Failure(DesignTemplateErrors.SystemTemplateReadOnly());
        }

        if (template.UserId != userId)
        {
            return Result.Failure(DesignTemplateErrors.NotFound());
        }

        if (await repository.IsUsedByActiveBatchAsync(template.Id, cancellationToken))
        {
            return Result.Failure(DesignTemplateErrors.ActiveBatchReference());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        template.IsActive = false;
        template.DeletedAt = now;
        template.UpdatedAt = now;
        await repository.TrySaveChangesAsync(cancellationToken);
        await InvalidatePersonalCacheAsync(userId.Value);

        return Result.Success();
    }

    public async Task<Result<DesignTemplateDetailResponseDto>> CloneAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.Unauthenticated());
        }

        var source = await repository.GetVisibleAsync(id, userId.Value, cancellationToken);
        if (source is null)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(DesignTemplateErrors.NotFound());
        }

        if (source.IsSystemTemplate != true)
        {
            return Result.Failure<DesignTemplateDetailResponseDto>(
                DesignTemplateErrors.CloneRequiresSystemTemplate());
        }

        for (var attempt = 0; attempt < DesignTemplateRules.MaximumCloneWriteAttempts; attempt++)
        {
            var existingNames = (await repository.ListPersonalNamesAsync(userId.Value, cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cloneName = BuildCloneName(source.Name, existingNames);
            var now = timeProvider.GetUtcNow();
            var clone = new DesignTemplate
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = cloneName,
                Type = DesignTemplateTypes.Personal,
                NicheCategory = source.NicheCategory,
                ArtStyle = source.ArtStyle,
                BasePrompt = source.BasePrompt,
                NegativePrompt = source.NegativePrompt,
                ExamplePrompts = source.ExamplePrompts,
                StyleDescription = source.StyleDescription,
                PreviewImageUrl = source.PreviewImageUrl,
                IsSystemTemplate = false,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            };

            if (await repository.TryAddAsync(clone, cancellationToken))
            {
                await InvalidatePersonalCacheAsync(userId.Value);
                return Result.Success(MapDetail(MapCacheItem(clone)));
            }
        }

        return Result.Failure<DesignTemplateDetailResponseDto>(
            DesignTemplateErrors.CloneNameUnavailable());
    }

    public async Task<Result<ResolvedDesignTemplatePromptResponseDto>> ResolveForPromptAsync(
        Guid sellerId,
        ResolveDesignTemplatePromptRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var template = await repository.GetVisibleAsync(request.TemplateId, sellerId, cancellationToken);
        if (template is null)
        {
            return Result.Failure<ResolvedDesignTemplatePromptResponseDto>(DesignTemplateErrors.NotFound());
        }

        var subject = request.Subject.Trim();
        var niche = request.Niche.Trim();
        var style = request.ArtStyleOverride ?? template.ArtStyle;
        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(niche) ||
            style is null ||
            !DesignTemplateArtStyles.Labels.TryGetValue(style, out var styleLabel))
        {
            return Result.Failure<ResolvedDesignTemplatePromptResponseDto>(
                DesignTemplateErrors.PromptUnresolved());
        }

        var keywords = request.Keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keywords.Length == 0 &&
            DesignTemplateNiches.DefaultKeywords.TryGetValue(niche, out var defaults))
        {
            keywords = defaults.ToArray();
        }

        if (keywords.Length == 0)
        {
            keywords = ["clean supporting motifs"];
        }

        var positivePrompt = ReplacePromptPlaceholders(
            template.BasePrompt,
            subject,
            niche,
            styleLabel,
            keywords);
        if (DesignTemplatePromptRules.ContainsUnresolvedPlaceholder(positivePrompt))
        {
            return Result.Failure<ResolvedDesignTemplatePromptResponseDto>(
                DesignTemplateErrors.PromptUnresolved());
        }

        var negativePrompt = template.NegativePrompt is null
            ? null
            : ReplacePromptPlaceholders(
                template.NegativePrompt,
                subject,
                niche,
                styleLabel,
                keywords);
        if (negativePrompt is not null && DesignTemplatePromptRules.ContainsUnresolvedPlaceholder(negativePrompt))
        {
            return Result.Failure<ResolvedDesignTemplatePromptResponseDto>(
                DesignTemplateErrors.PromptUnresolved());
        }

        await repository.IncrementUsageCountAsync(template.Id, cancellationToken);
        await TryRemoveCacheAsync(template.IsSystemTemplate == true
            ? DesignTemplateCacheKeys.SystemCatalog
            : DesignTemplateCacheKeys.PersonalCatalog(sellerId));

        return Result.Success(new ResolvedDesignTemplatePromptResponseDto(
            template.Id,
            positivePrompt,
            negativePrompt,
            DeserializeExamples(template.ExamplePrompts)));
    }

    private static string ReplacePromptPlaceholders(
        string prompt,
        string subject,
        string niche,
        string styleLabel,
        IReadOnlyCollection<string> keywords) =>
        prompt
            .Replace("{subject}", subject, StringComparison.Ordinal)
            .Replace("{niche}", niche, StringComparison.Ordinal)
            .Replace("{style}", styleLabel.ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("{keywords}", string.Join(", ", keywords), StringComparison.Ordinal);

    private Task<IReadOnlyList<DesignTemplateCacheItem>> GetSystemCatalogAsync(
        CancellationToken cancellationToken) =>
        GetCatalogAsync(
            DesignTemplateCacheKeys.SystemCatalog,
            DesignTemplateRules.SystemCacheExpiration,
            repository.ListSystemAsync,
            cancellationToken);

    private Task<IReadOnlyList<DesignTemplateCacheItem>> GetPersonalCatalogAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        GetCatalogAsync(
            DesignTemplateCacheKeys.PersonalCatalog(userId),
            DesignTemplateRules.PersonalCacheExpiration,
            token => repository.ListPersonalAsync(userId, token),
            cancellationToken);

    private async Task<IReadOnlyList<DesignTemplateCacheItem>> GetCatalogAsync(
        string cacheKey,
        TimeSpan cacheExpiration,
        Func<CancellationToken, Task<IReadOnlyList<DesignTemplate>>> loadFromDatabase,
        CancellationToken cancellationToken)
    {
        var cached = await TryReadCacheAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var loaded = (await loadFromDatabase(cancellationToken))
            .Select(MapCacheItem)
            .ToArray();
        await TryWriteCacheAsync(cacheKey, loaded, cacheExpiration, cancellationToken);

        return loaded;
    }

    private async Task<IReadOnlyList<DesignTemplateCacheItem>?> TryReadCacheAsync(
        string key,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            DesignTemplateRules.CacheOperationTimeout,
            timeProvider);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            return await cache.GetAsync<DesignTemplateCacheItem[]>(key, linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Design-template cache payload is invalid for {CacheKey}", key);
            await TryRemoveCacheAsync(key);
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Design-template cache read failed for {CacheKey}", key);
            return null;
        }
    }

    private async Task TryWriteCacheAsync(
        string key,
        IReadOnlyList<DesignTemplateCacheItem> value,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            DesignTemplateRules.CacheOperationTimeout,
            timeProvider);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            await cache.SetAsync(key, value, expiration, linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Design-template cache write failed for {CacheKey}", key);
        }
    }

    private Task InvalidatePersonalCacheAsync(Guid userId) =>
        TryRemoveCacheAsync(DesignTemplateCacheKeys.PersonalCatalog(userId));

    private async Task TryRemoveCacheAsync(string key)
    {
        using var timeout = new CancellationTokenSource(DesignTemplateRules.CacheInvalidationTimeout);
        try
        {
            await cache.RemoveAsync(key, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Design-template cache invalidation failed for {CacheKey}", key);
        }
    }

    private DesignTemplateCacheItem MapCacheItem(DesignTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.NicheCategory,
            template.ArtStyle,
            template.BasePrompt,
            template.NegativePrompt,
            DeserializeExamples(template.ExamplePrompts),
            template.StyleDescription,
            template.PreviewImageUrl,
            template.IsSystemTemplate == true,
            template.UsageCount ?? 0,
            ToUtcOffset(template.CreatedAt),
            ToUtcOffset(template.UpdatedAt));

    private IReadOnlyList<DesignTemplateExampleDto> DeserializeExamples(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DesignTemplateExampleDto[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "A design template contains invalid example prompt JSON");
            return [];
        }
    }

    private static string SerializeExamples(IReadOnlyList<DesignTemplateExampleDto> examples) =>
        JsonSerializer.Serialize(examples, JsonOptions);

    private static DesignTemplateSummaryResponseDto MapSummary(DesignTemplateCacheItem template) =>
        new(
            template.Id,
            template.Name,
            template.NicheCategory,
            template.ArtStyle,
            template.StyleDescription,
            template.PreviewImageUrl,
            template.IsSystemTemplate,
            template.UsageCount,
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            !template.IsSystemTemplate,
            !template.IsSystemTemplate,
            template.IsSystemTemplate);

    private static DesignTemplateDetailResponseDto MapDetail(DesignTemplateCacheItem template) =>
        new(
            template.Id,
            template.Name,
            template.NicheCategory,
            template.ArtStyle,
            template.BasePrompt,
            template.NegativePrompt,
            template.Examples,
            template.StyleDescription,
            template.PreviewImageUrl,
            template.IsSystemTemplate,
            template.UsageCount,
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            !template.IsSystemTemplate,
            !template.IsSystemTemplate,
            template.IsSystemTemplate);

    private Guid? GetAuthenticatedUserId() =>
        currentUser.IsAuthenticated ? currentUser.UserId : null;

    private static string NormalizeText(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeNameForLookup(string value) => value.Trim().ToLowerInvariant();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildPersonalDescription(string artStyle, string niche) =>
        $"A personal {DesignTemplateArtStyles.Labels[artStyle].ToLowerInvariant()} direction for the {niche} niche.";

    private static string BuildCloneName(string sourceName, IReadOnlySet<string> existingNames)
    {
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? " Copy" : $" Copy {number}";
            var maximumSourceLength = DesignTemplateRules.MaximumNameLength - suffix.Length;
            var baseName = sourceName[..Math.Min(sourceName.Length, maximumSourceLength)].TrimEnd();
            var candidate = $"{baseName}{suffix}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static DateTimeOffset ToUtcOffset(DateTime? value)
    {
        var timestamp = value ?? DateTime.UnixEpoch;
        return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
    }
}

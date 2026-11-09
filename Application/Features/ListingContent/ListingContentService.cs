using System.Diagnostics;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.UsageStatistics;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.Features.ListingContent;

public sealed class ListingContentService(
    ICurrentUser currentUser,
    IRepository<Product> products,
    IRepository<ListingContentEntity> listings,
    IRepository<ListingTitle> titles,
    IRepository<ListingDescription> descriptions,
    IRepository<ListingTag> tags,
    IRepository<ListingTagItem> tagItems,
    IRepository<ListingGenerationHistory> histories,
    IRepository<SeoScore> seoScores,
    IRepository<ApiUsageRecord> usageRecords,
    IRagService ragService,
    IPolicyContextRetriever policyContextRetriever,
    IListingContextRetriever listingContextRetriever,
    IGeminiTextGenerationProvider generator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ListingContentService> logger,
    IPlanEntitlementService entitlements) : IListingContentService
{
    public async Task<Result<ListingContentResponseDto>> GenerateAsync(
        Guid productId,
        CancellationToken cancellationToken = default,
        ListingSeoFeedbackDto? seoFeedback = null)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<ListingContentResponseDto>(Error.Unauthorized("ListingContent.Unauthenticated", "Please sign in to generate listing content."));

        var product = await products.Query()
            .Where(item => item.Id == productId && item.UserId == userId && item.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
            return Result.Failure<ListingContentResponseDto>(Error.NotFound("ListingContent.ProductNotFound", "The product was not found."));

        // Listing content is a plan feature, and so is writing a listing again; the Admin sets which plans have them.
        var listingCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.WorkflowListing, cancellationToken);
        if (listingCheck.IsFailure) return Result.Failure<ListingContentResponseDto>(listingCheck.Error);
        var isRegeneration = seoFeedback is not null
            || await listings.Query().AnyAsync(item => item.ProductId == productId, cancellationToken);
        if (isRegeneration)
        {
            var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.ListingRegenerate, cancellationToken);
            if (planCheck.IsFailure) return Result.Failure<ListingContentResponseDto>(planCheck.Error);
        }
        // Free, the trial and a plan with a monthly listing limit (such as Starter) count every listing,
        // regenerations included.
        var allowance = await entitlements.RequireAllowanceAsync(userId, UsageAllowances.Listings, cancellationToken: cancellationToken);
        if (allowance.IsFailure) return Result.Failure<ListingContentResponseDto>(allowance.Error);
        // Listings run on the platform's Gemini key, so they count toward the daily safety cap.
        var capCheck = await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyListingCap, cancellationToken: cancellationToken);
        if (capCheck.IsFailure) return Result.Failure<ListingContentResponseDto>(capCheck.Error);

        var input = BuildInput(product);
        var query = BuildRetrievalQuery(input);
        // Both retrievers share the request-scoped DbContext. Keep them sequential so a missing
        // pgvector extension or a slow RAG store cannot create concurrent DbContext operations.
        var policyContext = await SearchContextSafelyAsync(
            () => policyContextRetriever.SearchAsync(query, 5, cancellationToken), "policy", productId, cancellationToken);
        var listingContext = await SearchContextSafelyAsync(
            () => listingContextRetriever.SearchAsync(query, 3, cancellationToken, input.Niche), "listing", productId, cancellationToken);
        var prompt = ListingPromptBuilder.Build(input, policyContext, listingContext);
        if (seoFeedback is not null)
            prompt = $"{prompt}\n\n{BuildSeoRegenerationInstructions(seoFeedback)}";
        var stopwatch = Stopwatch.StartNew();
        GeminiTextGenerationResult generated;
        try
        {
            generated = await generator.GenerateAsync(new GeminiTextGenerationRequest(prompt, ResponseSchema: ListingPromptBuilder.ResponseSchema()), cancellationToken);
        }
        catch (HttpRequestException error) when (error.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout)
        {
            // Still overloaded after the provider's own retries: nothing was generated, so the seller can simply try again.
            logger.LogWarning(error, "Gemini was overloaded while generating the listing for product {ProductId}", productId);
            return Result.Failure<ListingContentResponseDto>(Error.Failure("ListingContent.ProviderBusy", "Gemini is overloaded right now. Wait a minute, then generate the listing again."));
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            // Refused or unusable for a reason the seller cannot act on (the key and model are the platform's).
            logger.LogError(error, "Gemini could not generate the listing for product {ProductId}", productId);
            return Result.Failure<ListingContentResponseDto>(Error.Failure("ListingContent.GenerationFailed", "Listing content could not be generated right now. Try again in a moment."));
        }
        stopwatch.Stop();
        GeneratedListingContent parsed;
        try
        {
            parsed = ListingContentParser.Parse(generated.Text);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // The model answered, but not with a title, a description and exactly 13 tags.
            logger.LogWarning(error, "Gemini returned an unusable listing for product {ProductId}", productId);
            return Result.Failure<ListingContentResponseDto>(Error.Failure("ListingContent.IncompleteResponse", "Gemini returned an incomplete listing. Generate it again."));
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var usageRecord = new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, ProductId = productId,
            Provider = "gemini", Feature = UsageFeatures.Listing,
            ModelName = generated.Model, ProviderRequestId = generated.ProviderRequestId,
            RequestUnits = 1, TokensInput = generated.InputTokens,
            TokensOutput = generated.OutputTokens, CostUsd = 0,
            LatencyMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
            Status = "success", CreatedAt = now
        };
        await usageRecords.AddAsync(usageRecord, cancellationToken: cancellationToken);

        var listing = await listings.Query()
            .Where(item => item.ProductId == productId)
            .SingleOrDefaultAsync(cancellationToken);
        var isNewListing = listing is null;
        var version = listing is null
            ? 1
            : await NextVersionAsync(listing.Id, cancellationToken);

        listing ??= new ListingContentEntity
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AiModelUsed = generated.Model,
            ModelVersion = generated.Model,
            ApprovalStatus = "pending",
            CreatedAt = now
        };
        listing.AiModelUsed = generated.Model;
        listing.ModelVersion = generated.Model;
        listing.ApiUsageRecordId = usageRecord.Id;
        listing.GenerationTimeSeconds = (decimal)stopwatch.Elapsed.TotalSeconds;
        listing.ApprovalStatus = "pending";
        listing.UpdatedAt = now;

        if (listing.CreatedAt is null)
            listing.CreatedAt = now;

        if (listing.Id == Guid.Empty)
            listing.Id = Guid.NewGuid();

        if (listing.ProductId == Guid.Empty)
            listing.ProductId = productId;

        if (isNewListing)
            await listings.AddAsync(listing, cancellationToken: cancellationToken);
        else
            await listings.UpdateAsync(listing, cancellationToken: cancellationToken);

        await titles.AddAsync(new ListingTitle
        {
            Id = Guid.NewGuid(), ListingContentId = listing.Id,
            AiGeneratedTitle = parsed.Title, CurrentTitle = parsed.Title,
            IsUserEdited = false, CharacterCount = parsed.Title.Length,
            IncludesPrimaryKeyword = input.Keywords.Any(keyword => parsed.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
            SeoScore = 0, VersionNumber = version, CreatedAt = now
        }, cancellationToken: cancellationToken);

        await descriptions.AddAsync(new ListingDescription
        {
            Id = Guid.NewGuid(), ListingContentId = listing.Id,
            AiGeneratedDescription = parsed.Description, CurrentDescription = parsed.Description,
            IsUserEdited = false, WordCount = parsed.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            StructureFollowed = JsonSerializer.Serialize(new { standard = true }), KeywordDensityOptimal = true,
            SeoScore = 0, VersionNumber = version, CreatedAt = now
        }, cancellationToken: cancellationToken);

        var listingTags = new ListingTag
        {
            Id = Guid.NewGuid(), ListingContentId = listing.Id,
            TagCount = parsed.Tags.Count, TagTypeDistribution = JsonSerializer.Serialize(new { ai = parsed.Tags.Count }),
            SeoScore = 0, IsUserEdited = false, VersionNumber = version, CreatedAt = now
        };
        await tags.AddAsync(listingTags, cancellationToken: cancellationToken);
        for (var index = 0; index < parsed.Tags.Count; index++)
        {
            var tag = parsed.Tags[index];
            await tagItems.AddAsync(new ListingTagItem
            {
                Id = Guid.NewGuid(), ListingTagId = listingTags.Id,
                TagValue = tag.Value, NormalizedValue = tag.Value.ToLowerInvariant(),
                TagType = NormalizeTagType(tag.Type), Position = index + 1, Source = "ai", CreatedAt = now
            }, cancellationToken: cancellationToken);
        }

        await histories.AddAsync(new ListingGenerationHistory
        {
            Id = Guid.NewGuid(), ListingContentId = listing.Id,
            ApiUsageRecordId = usageRecord.Id,
            GenerationNumber = version, PromptUsed = prompt, RawAiResponse = generated.Text,
            TitleGenerated = parsed.Title, DescriptionGenerated = parsed.Description,
            TagsGenerated = parsed.Tags.Select(tag => tag.Value).ToList(),
            UserAction = "regenerated", CreatedAt = now
        }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new ListingContentResponseDto(
            productId, listing.Id, parsed.Title, parsed.Description,
            parsed.Tags.Select((tag, index) => new ListingContentTagDto(tag.Value, tag.Type, index + 1)).ToArray(),
            version, generated.Model, now));
    }

    public async Task<Result<ListingRetrievalEvaluationResponse>> EvaluateRetrievalForAdminAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await products.Query()
            .Where(item => item.Id == productId && item.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
            return Result.Failure<ListingRetrievalEvaluationResponse>(Error.NotFound(
                "ListingContent.ProductNotFound",
                "The product was not found."));

        var input = BuildInput(product);
        var query = BuildRetrievalQuery(input);
        var stopwatch = Stopwatch.StartNew();
        var policyContext = await ragService.SearchWithDiagnosticsAsync(
            new RagSearchRequest(query, 5, ContentType: "policy"),
            cancellationToken);
        var listingContext = await ragService.SearchWithDiagnosticsAsync(
            new RagSearchRequest(query, 3, ContentType: "listing", Niche: input.Niche),
            cancellationToken);
        stopwatch.Stop();

        return Result.Success(new ListingRetrievalEvaluationResponse(
            product.Id,
            input.Name,
            input.ProductType,
            input.Niche,
            input.TargetAudience,
            input.Description,
            input.Keywords,
            input.Notes,
            query,
            stopwatch.Elapsed.TotalMilliseconds,
            policyContext,
            listingContext));
    }

    private static string BuildSeoRegenerationInstructions(ListingSeoFeedbackDto feedback)
    {
        var suggestions = feedback.ImprovementSuggestions.Count == 0
            ? "No specific suggestions were provided. Improve the weakest scored sections while preserving valid product facts."
            : string.Join("\n", feedback.ImprovementSuggestions.Select(item => $"- {item}"));

        return $"""
SEO feedback from the previous listing version:
- Overall score: {feedback.OverallScore}/100
- Title score: {feedback.TitleScore}/100
- Tags score: {feedback.TagsScore}/100
- Description score: {feedback.DescriptionScore}/100
- Keyword optimization score: {feedback.KeywordOptimizationScore}/100
- Tag relevance score: {feedback.TagRelevanceScore}/100
- Keyword density: {feedback.KeywordDensity}% of description words (keep it at or below {ListingSeoScorer.MaxKeywordDensity}%)
- Recommendation: {feedback.Recommendation}

Improve these weak areas in the new listing:
{suggestions}

Regeneration rules:
- Fix the weak sections instead of repeating the previous content.
- Keep all valid product facts and the product's intended audience.
- Use keywords naturally; do not keyword-stuff.
- Do not invent claims, certifications, materials, shipping promises, or guarantees.
- Return the complete listing in the required JSON format.
"""
;
    }

    public async Task<Result<ListingContentResponseDto>> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<ListingContentResponseDto>(Error.Unauthorized("ListingContent.Unauthenticated", "Please sign in to view listing content."));

        var row = await products.Query()
            .Where(product => product.Id == productId && product.UserId == userId && product.DeletedAt == null)
            .Select(product => product.ListingContent)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return Result.Failure<ListingContentResponseDto>(Error.NotFound("ListingContent.NotFound", "Listing content has not been generated."));

        var title = await titles.Query().Where(item => item.ListingContentId == row.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        var description = await descriptions.Query().Where(item => item.ListingContentId == row.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        var tag = await tags.Query().Where(item => item.ListingContentId == row.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        if (title is null || description is null || tag is null)
            return Result.Failure<ListingContentResponseDto>(Error.Failure("ListingContent.InvalidState", "Listing content is incomplete."));

        var tagRows = await tagItems.Query().Where(item => item.ListingTagId == tag.Id).OrderBy(item => item.Position).ToListAsync(cancellationToken);
        return Result.Success(new ListingContentResponseDto(
            productId, row.Id, title.CurrentTitle, description.CurrentDescription,
            tagRows.Select(item => new ListingContentTagDto(item.TagValue, item.TagType, item.Position)).ToArray(),
            title.VersionNumber, row.ModelVersion, row.UpdatedAt ?? row.CreatedAt));
    }

    public async Task<Result<ListingSeoScoreResponseDto>> EvaluateSeoAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<ListingSeoScoreResponseDto>(Error.Unauthorized("ListingContent.Unauthenticated", "Please sign in to evaluate listing SEO."));

        var product = await products.Query()
            .Where(item => item.Id == productId && item.UserId == userId && item.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
            return Result.Failure<ListingSeoScoreResponseDto>(Error.NotFound("ListingContent.ProductNotFound", "The product was not found."));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.ListingRegenerate, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<ListingSeoScoreResponseDto>(planCheck.Error);

        var listing = await listings.Query().Where(item => item.ProductId == productId).SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
            return Result.Failure<ListingSeoScoreResponseDto>(Error.NotFound("ListingContent.NotFound", "Generate listing content before evaluating SEO."));

        var title = await titles.Query().Where(item => item.ListingContentId == listing.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        var description = await descriptions.Query().Where(item => item.ListingContentId == listing.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        var tagSet = await tags.Query().Where(item => item.ListingContentId == listing.Id).OrderByDescending(item => item.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        if (title is null || description is null || tagSet is null)
            return Result.Failure<ListingSeoScoreResponseDto>(Error.Failure("ListingContent.InvalidState", "Listing content is incomplete."));

        var tagRows = await tagItems.Query().Where(item => item.ListingTagId == tagSet.Id).OrderBy(item => item.Position).ToListAsync(cancellationToken);
        var tagValues = tagRows.Select(item => item.TagValue).ToArray();
        var (overall, titleScore, tagsScore, descriptionScore, keywordOptimizationScore, tagRelevanceScore, keywordDensity,
            suggestions, recommendation, shouldRegenerate) = ListingSeoScorer.Evaluate(
                title.CurrentTitle, description.CurrentDescription, tagValues, ParseKeywords(product.MainKeywords));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var score = await seoScores.Query().Where(item => item.ListingContentId == listing.Id).SingleOrDefaultAsync(cancellationToken);
        score ??= new SeoScore { Id = Guid.NewGuid(), ListingContentId = listing.Id };
        score.OverallSeoScore = overall; score.TitleScore = titleScore; score.TagsScore = tagsScore;
        score.DescriptionScore = descriptionScore; score.KeywordOptimizationScore = keywordOptimizationScore;
        score.TagRelevanceScore = tagRelevanceScore; score.KeywordDensity = keywordDensity;
        score.ImprovementSuggestions = JsonSerializer.Serialize(suggestions);
        score.ScoringAlgorithmVersion = ListingSeoScorer.Version; score.CalculatedAt = now; score.UpdatedAt = now;
        if (score.Id == Guid.Empty) score.Id = Guid.NewGuid();
        if (score.ListingContentId == Guid.Empty) score.ListingContentId = listing.Id;
        if (await seoScores.Query().AnyAsync(item => item.Id == score.Id, cancellationToken)) await seoScores.UpdateAsync(score, cancellationToken: cancellationToken);
        else await seoScores.AddAsync(score, cancellationToken: cancellationToken);
        title.SeoScore = titleScore; description.SeoScore = descriptionScore; tagSet.SeoScore = tagsScore;
        await titles.UpdateAsync(title, cancellationToken: cancellationToken);
        await descriptions.UpdateAsync(description, cancellationToken: cancellationToken);
        await tags.UpdateAsync(tagSet, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ListingSeoScoreResponseDto(productId, listing.Id, overall, titleScore, tagsScore, descriptionScore,
            keywordOptimizationScore, tagRelevanceScore, keywordDensity, suggestions, recommendation, shouldRegenerate, ListingSeoScorer.Version, now));
    }

    private async Task<int> NextVersionAsync(Guid listingContentId, CancellationToken cancellationToken)
    {
        var titleVersion = await titles.Query().Where(item => item.ListingContentId == listingContentId).Select(item => (int?)item.VersionNumber).MaxAsync(cancellationToken) ?? 0;
        var descriptionVersion = await descriptions.Query().Where(item => item.ListingContentId == listingContentId).Select(item => (int?)item.VersionNumber).MaxAsync(cancellationToken) ?? 0;
        var tagVersion = await tags.Query().Where(item => item.ListingContentId == listingContentId).Select(item => (int?)item.VersionNumber).MaxAsync(cancellationToken) ?? 0;
        return Math.Max(titleVersion, Math.Max(descriptionVersion, tagVersion)) + 1;
    }

    private static IReadOnlyList<string> ParseKeywords(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(13).ToArray();

    private static ListingContentInput BuildInput(Product product) => new(
        product.Id,
        product.Name,
        product.ProductType,
        product.NicheCategory,
        product.TargetAudience,
        product.InputDescription,
        ParseKeywords(product.MainKeywords),
        product.Notes);

    private static string BuildRetrievalQuery(ListingContentInput input) =>
        $"{input.Name}; {input.ProductType}; {input.Niche}; {input.TargetAudience}; {string.Join(", ", input.Keywords)}; {input.Description}; {input.Notes}";

    private static string NormalizeTagType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "primary" or "long_tail" or "niche" or "occasion" or "product_type" => value.Trim().ToLowerInvariant(),
        _ => "long_tail"
    };

    private async Task<IReadOnlyList<APCS.Application.Features.Rag.Dtos.RagSearchResultDto>> SearchContextSafelyAsync(
        Func<Task<IReadOnlyList<APCS.Application.Features.Rag.Dtos.RagSearchResultDto>>> search,
        string contextType,
        Guid productId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await search();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RAG {ContextType} retrieval failed for product {ProductId}; using listing defaults.", contextType, productId);
            return [];
        }
    }
}

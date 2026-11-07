using System.Globalization;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

public sealed class MarketResearchService(
    ICommerceMarketDataConnectorFactory connectors,
    IRagService ragService,
    IRepository<MarketListingSnapshot> snapshots,
    IRepository<MarketOpportunity> opportunities,
    IRepository<MarketOpportunityEvidence> evidence,
    IRepository<MarketQueryObservation> queryObservations,
    IUnitOfWork unitOfWork) : IMarketResearchService
{
    public async Task<IReadOnlyList<string>> SelectUnobservedQueriesAsync(
        CommercePlatform platform,
        IReadOnlyList<string> queries,
        DateOnly observedOn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        var platformKey = MarketQueryKey.Platform(platform);
        var observed = await queryObservations.Query()
            .Where(item => item.Platform == platformKey && item.ObservedOn == observedOn)
            .Select(item => item.Query)
            .ToListAsync(cancellationToken);
        var observedKeys = observed.ToHashSet(StringComparer.Ordinal);
        return queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Where(query => !observedKeys.Contains(MarketQueryKey.Normalize(query)))
            .ToArray();
    }

    public async Task<MarketResearchResponse> RetrieveAndIngestAsync(
        MarketResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var queries = request.Queries
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        if (queries.Length == 0)
            throw new ArgumentException("At least one market research query is required.", nameof(request));

        var limit = Math.Clamp(request.LimitPerQuery, 1, 500);
        var search = await connectors.Create(request.Platform)
            .SearchPublicListingsAsync(new CommerceMarketSearchRequest(queries, limit), cancellationToken);
        var uniqueListings = search.Listings
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalListingId) && !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => item.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var selected = MarketResearchSelector.Select(uniqueListings, request.TopN, request.MinimumViralScore);
        var selectedByListingId = selected.ToDictionary(
            item => item.Listing.ExternalListingId,
            StringComparer.OrdinalIgnoreCase);

        var result = new List<MarketResearchItem>(selected.Count);
        var observedAt = DbNow();
        await RecordQueryObservationsAsync(request.Platform, search.Queries, observedAt, cancellationToken);
        foreach (var listing in uniqueListings)
        {
            selectedByListingId.TryGetValue(listing.ExternalListingId, out var selection);
            Guid? documentId = null;
            if (selection is not null)
            {
                var ragScore = selection.ViralScore;
                var sourceId = $"{listing.Platform.ToString().ToLowerInvariant()}:public-listing:{listing.ExternalListingId}";
                var content = BuildRagContent(listing, ragScore);
                documentId = await ragService.IngestAsync(new RagIngestRequest(
                    listing.Title,
                    $"{listing.Platform.ToString().ToLowerInvariant()}-public-listing",
                    listing.Url,
                    DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                    "en",
                    content,
                    new Dictionary<string, object?>
                    {
                        ["platform"] = listing.Platform.ToString().ToLowerInvariant(),
                        ["niche"] = listing.MatchedQuery,
                        ["sellerScope"] = "public",
                        ["externalListingId"] = listing.ExternalListingId,
                        ["externalSellerId"] = listing.ExternalSellerId,
                        ["rank"] = listing.Rank,
                        ["viralScore"] = ragScore,
                        ["favorites"] = listing.Favorites,
                        ["views"] = listing.Views,
                        ["collectedAtUtc"] = DateTimeOffset.UtcNow
                    },
                    RagContentTypes.Listing,
                    sourceId), cancellationToken);
            }

            var platform = listing.Platform.ToString().ToLowerInvariant();
            var snapshot = new MarketListingSnapshot
            {
                Id = Guid.NewGuid(),
                Platform = platform,
                ExternalListingId = listing.ExternalListingId,
                ExternalSellerId = listing.ExternalSellerId,
                Title = listing.Title,
                Description = listing.Description,
                SourceUrl = listing.Url,
                Price = listing.Price,
                Currency = listing.Currency,
                Quantity = listing.Quantity,
                Favorites = listing.Favorites,
                Views = listing.Views,
                Tags = JsonSerializer.Serialize(listing.Tags),
                MatchedQuery = listing.MatchedQuery,
                ObservedAt = observedAt,
                CollectedAt = observedAt,
                RawPayload = listing.RawPayload,
                RagDocumentId = documentId
            };
            await snapshots.AddAsync(snapshot, cancellationToken: cancellationToken);

            if (selection is null)
                continue;

            var chosen = selection;
            var score = chosen.ViralScore;
            var ragDocumentId = documentId!.Value;
            var niche = string.IsNullOrWhiteSpace(listing.MatchedQuery) ? listing.Title : listing.MatchedQuery.Trim();
            // A public listing can be returned by several search queries. Keep one
            // opportunity for the same platform/title and retain each query as evidence
            // instead of showing the same product concept more than once in the radar.
            var relatedOpportunities = await opportunities.FindAsync(x =>
                x.Platform == platform && x.Niche == niche, cancellationToken);
            var opportunity = relatedOpportunities
                .Select(candidate => new
                {
                    Opportunity = candidate,
                    Similarity = string.Equals(candidate.Title, listing.Title, StringComparison.OrdinalIgnoreCase)
                        ? 1d
                        : MarketOpportunityConceptMatcher.Similarity(candidate.Title, listing.Title)
                })
                .Where(item => item.Similarity >= 0.6)
                .OrderByDescending(item => item.Similarity)
                .ThenByDescending(item => item.Opportunity.MarketOpportunityScore)
                .ThenBy(item => item.Opportunity.Id)
                .Select(item => item.Opportunity)
                .FirstOrDefault();
            if (opportunity is null)
            {
                opportunity = new MarketOpportunity
                {
                    Id = Guid.NewGuid(),
                    Platform = platform,
                    Title = listing.Title,
                    Niche = niche,
                    Summary = listing.Description,
                    MarketOpportunityScore = (decimal)score,
                    Confidence = 0.5m,
                    ScoreBreakdown = JsonSerializer.Serialize(new
                    {
                        viralScore = score,
                        chosen.FavoritesScore,
                        chosen.ViewsScore,
                        formulaVersion = "viral-v1"
                    }),
                    FormulaVersion = "viral-v1",
                    Status = "active",
                    FirstObservedAt = observedAt,
                    LastObservedAt = observedAt,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                };
                await opportunities.AddAsync(opportunity, cancellationToken: cancellationToken);
            }
            else
            {
                opportunity.MarketOpportunityScore = Math.Max(opportunity.MarketOpportunityScore, (decimal)score);
                opportunity.LastObservedAt = observedAt;
                opportunity.UpdatedAt = observedAt;
                await opportunities.UpdateAsync(opportunity, cancellationToken: cancellationToken);
            }

            await evidence.AddAsync(new MarketOpportunityEvidence
            {
                OpportunityId = opportunity.Id,
                SnapshotId = snapshot.Id,
                RagDocumentId = ragDocumentId,
                EvidenceRole = "supporting",
                EvidenceRank = 1,
                CreatedAt = observedAt
            }, cancellationToken: cancellationToken);
            result.Add(new MarketResearchItem(
                listing.ExternalListingId,
                listing.Title,
                score,
                listing.Favorites,
                listing.Views,
                ragDocumentId,
                listing.Url,
                listing.MatchedQuery,
                new MarketResearchScoreBreakdown(
                        chosen.FavoritesScore,
                        chosen.ViewsScore,
                        chosen.FavoritesWeight,
                        chosen.ViewsWeight)));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MarketResearchResponse(request.Platform, uniqueListings.Length, result.Count, result);
    }

    /// <summary>
    /// Keeps one observation per platform, query and UTC day. A later run of the same day
    /// replaces the figures, so the row always holds that day's latest marketplace total.
    /// </summary>
    private async Task RecordQueryObservationsAsync(
        CommercePlatform platform,
        IReadOnlyList<CommerceMarketQueryResult> results,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var platformKey = MarketQueryKey.Platform(platform);
        var observedOn = DateOnly.FromDateTime(observedAt);
        foreach (var result in results)
        {
            var query = MarketQueryKey.Normalize(result.Query);
            if (query.Length == 0 || query.Length > MarketQueryKey.MaximumLength)
                continue;

            var existing = (await queryObservations.FindAsync(
                    item => item.Platform == platformKey && item.Query == query && item.ObservedOn == observedOn,
                    cancellationToken))
                .SingleOrDefault();
            if (existing is null)
            {
                await queryObservations.AddAsync(new MarketQueryObservation
                {
                    Id = Guid.NewGuid(),
                    Platform = platformKey,
                    Query = query,
                    ObservedOn = observedOn,
                    TotalResults = result.TotalResults,
                    SampleSize = result.SampleSize,
                    LimitUsed = Math.Max(1, result.LimitUsed),
                    SortOn = result.SortOn,
                    ObservedAt = observedAt
                }, cancellationToken: cancellationToken);
                continue;
            }

            existing.TotalResults = result.TotalResults ?? existing.TotalResults;
            existing.SampleSize = result.SampleSize;
            existing.LimitUsed = Math.Max(1, result.LimitUsed);
            existing.SortOn = result.SortOn;
            existing.ObservedAt = observedAt;
            await queryObservations.UpdateAsync(existing, cancellationToken: cancellationToken);
        }
    }

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static string BuildRagContent(CommerceMarketListing listing, double score)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Public marketplace listing reference from {listing.Platform}.");
        builder.AppendLine($"Title: {listing.Title}");
        if (!string.IsNullOrWhiteSpace(listing.Description)) builder.AppendLine($"Description: {listing.Description}");
        if (listing.Tags.Count > 0) builder.AppendLine($"Tags: {string.Join(", ", listing.Tags)}");
        if (listing.Price is not null) builder.AppendLine($"Price: {listing.Price} {listing.Currency}");
        builder.AppendLine($"Observed viral score: {score.ToString(CultureInfo.InvariantCulture)}/100");
        if (listing.Favorites is not null) builder.AppendLine($"Observed favorites: {listing.Favorites}");
        if (listing.Views is not null) builder.AppendLine($"Observed views: {listing.Views}");
        builder.AppendLine("Use this only as a trend, tone, structure, and keyword reference. Do not copy its wording or assume its product facts apply to another product.");
        return builder.ToString();
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

public sealed class MarketResearchService(
    ICommerceMarketDataConnectorFactory connectors,
    IRagService ragService,
    IRepository<MarketListingSnapshot> snapshots,
    IRepository<MarketOpportunity> opportunities,
    IRepository<MarketOpportunityEvidence> evidence,
    IUnitOfWork unitOfWork) : IMarketResearchService
{
    public async Task<MarketResearchResponse> RetrieveAndIngestAsync(
        MarketResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var queries = request.Queries
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        if (queries.Length == 0)
            throw new ArgumentException("At least one market research query is required.", nameof(request));

        var limit = Math.Clamp(request.LimitPerQuery, 1, 100);
        var listings = await connectors.Create(request.Platform)
            .SearchPublicListingsAsync(new CommerceMarketSearchRequest(queries, limit), cancellationToken);
        var uniqueListings = listings
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalListingId) && !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => item.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var selected = MarketResearchSelector.Select(uniqueListings, request.TopN, request.MinimumViralScore);

        var result = new List<MarketResearchItem>(selected.Count);
        foreach (var selection in selected)
        {
            var listing = selection.Listing;
            var score = selection.ViralScore;
            var sourceId = $"{listing.Platform.ToString().ToLowerInvariant()}:public-listing:{listing.ExternalListingId}";
            var content = BuildRagContent(listing, score);
            var documentId = await ragService.IngestAsync(new RagIngestRequest(
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
                    ["viralScore"] = score,
                    ["favorites"] = listing.Favorites,
                    ["views"] = listing.Views,
                    ["collectedAtUtc"] = DateTimeOffset.UtcNow
                },
                RagContentTypes.Listing,
                sourceId), cancellationToken);
            var observedAt = DbNow();
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
                RagDocumentId = documentId
            };
            await snapshots.AddAsync(snapshot, cancellationToken: cancellationToken);

            var niche = string.IsNullOrWhiteSpace(listing.MatchedQuery) ? listing.Title : listing.MatchedQuery.Trim();
            var opportunity = (await opportunities.FindAsync(x =>
                x.Platform == platform && x.Niche == niche && x.Title == listing.Title, cancellationToken)).SingleOrDefault();
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
                        selection.FavoritesScore,
                        selection.ViewsScore,
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
                RagDocumentId = documentId,
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
                documentId,
                listing.Url,
                listing.MatchedQuery,
                new MarketResearchScoreBreakdown(
                        selection.FavoritesScore,
                        selection.ViewsScore,
                        selection.FavoritesWeight,
                        selection.ViewsWeight)));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MarketResearchResponse(request.Platform, uniqueListings.Length, result.Count, result);
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

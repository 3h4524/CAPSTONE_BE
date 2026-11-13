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
    IUnitOfWork unitOfWork,
    IMarketListingQueries? listingQueries = null) : IMarketResearchService
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
        // Every listing is kept as a snapshot, but only physical products can become opportunities.
        var selected = MarketResearchSelector.Select(
            uniqueListings.Where(item => !MarketListingKind.IsDigital(item.RawPayload)).ToArray(),
            request.TopN,
            request.MinimumViralScore);
        var selectedByListingId = selected.ToDictionary(
            item => item.Listing.ExternalListingId,
            StringComparer.OrdinalIgnoreCase);

        var result = new List<MarketResearchItem>(selected.Count);
        var observedAt = DbNow();
        await RecordQueryObservationsAsync(request.Platform, search.Queries, observedAt, cancellationToken);

        // A listing already behind an opportunity keeps backing it every day it is collected, also on days it is not
        // among the top picks, so the opportunity gathers observed days and its trend and confidence can be read.
        var platformKey = request.Platform.ToString().ToLowerInvariant();
        var listingIds = uniqueListings.Select(item => item.ExternalListingId).ToArray();
        var linkedRows = await evidence.Query()
            .Where(row => row.Snapshot.Platform == platformKey && listingIds.Contains(row.Snapshot.ExternalListingId)
                && row.Opportunity.Status != MarketOpportunityLifecycle.Archived && !row.Opportunity.IsSynthetic)
            .Select(row => new { row.Snapshot.ExternalListingId, row.OpportunityId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var linkedByListing = linkedRows
            .GroupBy(row => row.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(row => row.OpportunityId).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        var linkedIds = linkedRows.Select(row => row.OpportunityId).Distinct().ToArray();
        var linkedOpportunities = linkedIds.Length == 0
            ? new Dictionary<Guid, MarketOpportunity>()
            : (await opportunities.FindAsync(x => linkedIds.Contains(x.Id), cancellationToken)).ToDictionary(x => x.Id);
        var touched = new HashSet<Guid>();

        // One snapshot per listing, query and UTC day: a second collection the same day (another worker on the shared
        // database, a manual run, a retried batch) refreshes that day's figures instead of counting the listing twice.
        var dayStart = observedAt.Date;
        var dayEnd = dayStart.AddDays(1);
        var todays = await snapshots.FindAsync(row => row.Platform == platformKey && listingIds.Contains(row.ExternalListingId)
            && row.ObservedAt >= dayStart && row.ObservedAt < dayEnd && !row.IsSynthetic, cancellationToken);
        var todaysByKey = todays
            .GroupBy(row => SnapshotKey(row.ExternalListingId, row.MatchedQuery))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.CollectedAt).First());
        var todaysIds = todays.Select(row => row.Id).ToArray();
        var evidencePairs = todaysIds.Length == 0
            ? new HashSet<(Guid, Guid)>()
            : (await evidence.Query()
                .Where(row => todaysIds.Contains(row.SnapshotId))
                .Select(row => new { row.OpportunityId, row.SnapshotId })
                .ToListAsync(cancellationToken))
                .Select(row => (row.OpportunityId, row.SnapshotId))
                .ToHashSet();

        // A snapshot backs an opportunity once, however often it is collected that day.
        async Task AddEvidenceAsync(Guid opportunityId, Guid snapshotId, Guid? ragDocumentId)
        {
            if (!evidencePairs.Add((opportunityId, snapshotId)))
                return;
            await evidence.AddAsync(new MarketOpportunityEvidence
            {
                OpportunityId = opportunityId,
                SnapshotId = snapshotId,
                RagDocumentId = ragDocumentId,
                EvidenceRole = "supporting",
                EvidenceRank = 1,
                CreatedAt = observedAt
            }, cancellationToken: cancellationToken);
        }

        // Today's snapshot of a listing backs every opportunity it already backed (but the one it was just matched to).
        async Task BackKnownOpportunitiesAsync(string listingId, Guid snapshotId, Guid? matchedId)
        {
            foreach (var linkedId in linkedByListing.GetValueOrDefault(listingId) ?? [])
            {
                if (linkedId == matchedId || !linkedOpportunities.TryGetValue(linkedId, out var linked))
                    continue;
                MarkObserved(linked, observedAt);
                await opportunities.UpdateAsync(linked, cancellationToken: cancellationToken);
                await AddEvidenceAsync(linked.Id, snapshotId, null);
                touched.Add(linked.Id);
            }
        }

        // Each niche's product concepts, told apart by the distinctive tags of the top listings that made or matched
        // them; the listings that only joined later do not widen a concept.
        var nicheNames = uniqueListings.Select(NicheOf).Distinct(StringComparer.Ordinal).ToArray();
        var nicheOpportunities = await opportunities.FindAsync(x => x.Platform == platformKey && nicheNames.Contains(x.Niche) && !x.IsSynthetic, cancellationToken);
        var opportunityById = nicheOpportunities.ToDictionary(x => x.Id);
        var nicheIds = opportunityById.Keys.ToArray();
        var signatureRows = await evidence.Query()
            .Where(row => nicheIds.Contains(row.OpportunityId))
            .Select(row => new { row.OpportunityId, row.Snapshot.Tags, Picked = row.RagDocumentId != null })
            .ToListAsync(cancellationToken);
        var signatureByOpportunity = signatureRows.ToLookup(row => row.OpportunityId);
        var commonByNiche = uniqueListings
            .GroupBy(NicheOf, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => MarketConceptMatcher.CommonTags(group.Key, group.Select(item => (IEnumerable<string>)item.Tags).ToArray()),
                StringComparer.Ordinal);
        var conceptsByNiche = nicheOpportunities
            .GroupBy(item => item.Niche, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item =>
            {
                var rows = signatureByOpportunity[item.Id].ToArray();
                var picked = rows.Any(row => row.Picked) ? rows.Where(row => row.Picked) : rows;
                return new MarketConceptMatcher.Concept(item.Id, item.Title,
                    MarketConceptMatcher.DistinctiveTags(picked.SelectMany(row => ReadTags(row.Tags)), commonByNiche.GetValueOrDefault(group.Key) ?? []));
            }).ToList(), StringComparer.Ordinal);
        List<MarketConceptMatcher.Concept> ConceptsOf(string niche)
        {
            if (!conceptsByNiche.TryGetValue(niche, out var list))
                conceptsByNiche[niche] = list = [];
            return list;
        }
        HashSet<string> TagsOf(CommerceMarketListing listing) =>
            MarketConceptMatcher.DistinctiveTags(listing.Tags, commonByNiche.GetValueOrDefault(NicheOf(listing)) ?? []);

        // A listing outside the top picks that shows a known concept backs it too, so a concept gathers the listings
        // and shops that sell it instead of only the few that ranked first.
        async Task JoinConceptAsync(CommerceMarketListing listing, Guid snapshotId)
        {
            if (MarketListingKind.IsDigital(listing.RawPayload) || linkedByListing.ContainsKey(listing.ExternalListingId))
                return;
            var concept = MarketConceptMatcher.Match(listing.Title, TagsOf(listing),
                ConceptsOf(NicheOf(listing)).Where(item => opportunityById[item.OpportunityId].Status != MarketOpportunityLifecycle.Archived));
            if (concept is null)
                return;
            var joined = opportunityById[concept.OpportunityId];
            MarkObserved(joined, observedAt);
            await opportunities.UpdateAsync(joined, cancellationToken: cancellationToken);
            await AddEvidenceAsync(joined.Id, snapshotId, null);
            touched.Add(joined.Id);
        }

        foreach (var listing in uniqueListings)
        {
            selectedByListingId.TryGetValue(listing.ExternalListingId, out var selection);
            Guid? documentId = null;
            if (selection is not null)
            {
                var ragScore = selection.ViralScore;
                var sourceId = $"{listing.Platform.ToString().ToLowerInvariant()}:public-listing:{listing.ExternalListingId}";
                var content = BuildRagContent(listing);
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
            var key = SnapshotKey(listing.ExternalListingId, listing.MatchedQuery);
            if (todaysByKey.TryGetValue(key, out var snapshot))
            {
                // Collected again today: the row keeps the day's latest figures, as the query observation does.
                snapshot.ExternalSellerId = listing.ExternalSellerId;
                snapshot.Title = listing.Title;
                snapshot.Description = listing.Description;
                snapshot.SourceUrl = listing.Url;
                snapshot.Price = listing.Price;
                snapshot.Currency = listing.Currency;
                snapshot.Quantity = listing.Quantity;
                snapshot.Favorites = listing.Favorites;
                snapshot.Views = listing.Views;
                snapshot.Tags = JsonSerializer.Serialize(listing.Tags);
                snapshot.ObservedAt = observedAt;
                snapshot.CollectedAt = observedAt;
                snapshot.RawPayload = listing.RawPayload;
                snapshot.RagDocumentId = documentId ?? snapshot.RagDocumentId;
                await snapshots.UpdateAsync(snapshot, cancellationToken: cancellationToken);
            }
            else
            {
                snapshot = new MarketListingSnapshot
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
                todaysByKey[key] = snapshot;
            }

            if (selection is null)
            {
                await BackKnownOpportunitiesAsync(listing.ExternalListingId, snapshot.Id, null);
                await JoinConceptAsync(listing, snapshot.Id);
                continue;
            }

            var chosen = selection;
            var score = chosen.ViralScore;
            var ragDocumentId = documentId!.Value;
            var niche = NicheOf(listing);
            // One opportunity is one product concept of the niche: a top listing showing a known concept (shared
            // distinctive tags, or a nearly identical title) adds to it instead of opening another.
            var listingTags = TagsOf(listing);
            var concept = MarketConceptMatcher.Match(listing.Title, listingTags, ConceptsOf(niche));
            var opportunity = concept is null ? null : opportunityById[concept.OpportunityId];
            concept?.Tags.UnionWith(listingTags);
            if (opportunity is null)
            {
                opportunity = new MarketOpportunity
                {
                    Id = Guid.NewGuid(),
                    Platform = platform,
                    Title = listing.Title,
                    Niche = niche,
                    Category = MarketCategoryInference.FromText(niche) ?? MarketCategoryInference.FromText(listing.Title),
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
                    Status = MarketOpportunityLifecycle.Active,
                    FirstObservedAt = observedAt,
                    LastObservedAt = observedAt,
                    CreatedAt = observedAt,
                    UpdatedAt = observedAt
                };
                await opportunities.AddAsync(opportunity, cancellationToken: cancellationToken);
                opportunityById[opportunity.Id] = opportunity;
                ConceptsOf(niche).Add(new MarketConceptMatcher.Concept(opportunity.Id, opportunity.Title, listingTags));
            }
            else
            {
                // The latest day weighs in, so a concept that cools down loses score instead of keeping its best day.
                // Once a day: collecting it again the same day does not move the score further.
                if (opportunity.LastObservedAt < dayStart)
                    opportunity.MarketOpportunityScore = MarketOpportunityLifecycle.BlendScore(opportunity.MarketOpportunityScore, score);
                MarkObserved(opportunity, observedAt);
                await opportunities.UpdateAsync(opportunity, cancellationToken: cancellationToken);
            }
            touched.Add(opportunity.Id);
            await BackKnownOpportunitiesAsync(listing.ExternalListingId, snapshot.Id, opportunity.Id);
            await AddEvidenceAsync(opportunity.Id, snapshot.Id, ragDocumentId);
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
        await RefreshConfidenceAsync(touched, cancellationToken);
        await AgeUnobservedAsync(observedAt, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await CompactOldSnapshotsAsync(observedAt, cancellationToken);
        return new MarketResearchResponse(request.Platform, uniqueListings.Length, result.Count, result);
    }

    /// <summary>The niche a listing was collected for: its search query, or its title when it has none.</summary>
    private static string NicheOf(CommerceMarketListing listing) =>
        string.IsNullOrWhiteSpace(listing.MatchedQuery) ? listing.Title : listing.MatchedQuery.Trim();

    /// <summary>The tags a snapshot stored as a JSON array; none when it cannot be read.</summary>
    private static IEnumerable<string> ReadTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>A listing as one search query found it: its day's snapshot is kept once per pair.</summary>
    private static string SnapshotKey(string listingId, string? matchedQuery) =>
        $"{listingId.Trim().ToLowerInvariant()}\n{MarketQueryKey.Normalize(matchedQuery ?? string.Empty)}";

    /// <summary>
    /// Snapshots older than a week that a newer one of the same listing supersedes keep only their figures, so a day of
    /// collection does not add Etsy's full listing text for good. A run slims a bounded number; the rest follows on later runs.
    /// </summary>
    private async Task CompactOldSnapshotsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (listingQueries is null) return;
        var before = nowUtc.Date.AddDays(-MarketSnapshotRetention.FullTextDays);
        for (var batch = 0; batch < MarketSnapshotRetention.MaxBatchesPerRun; batch++)
        {
            if (await listingQueries.CompactSupersededSnapshotsAsync(before, MarketSnapshotRetention.BatchSize, cancellationToken) < MarketSnapshotRetention.BatchSize)
                return;
        }
    }

    /// <summary>An opportunity seen again is current: it is active, whatever it had aged to.</summary>
    private static void MarkObserved(MarketOpportunity opportunity, DateTime observedAt)
    {
        opportunity.LastObservedAt = observedAt;
        opportunity.UpdatedAt = observedAt;
        opportunity.Status = MarketOpportunityLifecycle.Active;
    }

    /// <summary>Recomputes how sure the radar is of each opportunity this run added evidence to, from all its evidence.</summary>
    private async Task RefreshConfidenceAsync(IReadOnlyCollection<Guid> touched, CancellationToken cancellationToken)
    {
        if (touched.Count == 0) return;
        var ids = touched.ToArray();
        var rows = await evidence.Query()
            .Where(row => ids.Contains(row.OpportunityId))
            .Select(row => new { row.OpportunityId, row.Snapshot.ExternalListingId, row.Snapshot.ExternalSellerId, row.Snapshot.ObservedAt, row.Snapshot.IsSynthetic })
            .ToListAsync(cancellationToken);
        var tracked = (await opportunities.FindAsync(x => ids.Contains(x.Id), cancellationToken)).ToDictionary(x => x.Id);
        foreach (var group in rows.GroupBy(row => row.OpportunityId))
        {
            if (!tracked.TryGetValue(group.Key, out var opportunity))
                continue;
            opportunity.Confidence = MarketOpportunityLifecycle.Confidence(
                group.Select(row => row.ExternalListingId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.Select(row => row.ObservedAt.Date).Distinct().Count(),
                group.Select(row => row.ExternalSellerId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.Count(row => !row.IsSynthetic),
                group.Count(row => row.IsSynthetic));
            await opportunities.UpdateAsync(opportunity, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Opportunities the collection has not seen for a while go stale, then archived, so the radar stops offering a
    /// niche the marketplace moved on from. Demo (synthetic) opportunities are left as they are.
    /// </summary>
    private async Task AgeUnobservedAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var staleBefore = nowUtc.AddDays(-MarketOpportunityLifecycle.StaleAfterDays);
        var ageing = await opportunities.FindAsync(x => x.Status != MarketOpportunityLifecycle.Archived && !x.IsSynthetic && x.LastObservedAt < staleBefore, cancellationToken);
        foreach (var opportunity in ageing)
        {
            var status = MarketOpportunityLifecycle.StatusFor(opportunity.Status, opportunity.LastObservedAt, nowUtc);
            if (status == opportunity.Status)
                continue;
            opportunity.Status = status;
            opportunity.UpdatedAt = nowUtc;
            await opportunities.UpdateAsync(opportunity, cancellationToken: cancellationToken);
        }
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

    /// <summary>
    /// Only the stable listing text is embedded. Score, favorites and views change every day; in the
    /// text they would change the content hash and force a new version with fresh embeddings on each
    /// collection. They stay in the document metadata, which an unchanged document refreshes in place.
    /// </summary>
    private static string BuildRagContent(CommerceMarketListing listing)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Public marketplace listing reference from {listing.Platform}.");
        builder.AppendLine($"Title: {listing.Title}");
        if (!string.IsNullOrWhiteSpace(listing.Description)) builder.AppendLine($"Description: {listing.Description}");
        if (listing.Tags.Count > 0) builder.AppendLine($"Tags: {string.Join(", ", listing.Tags)}");
        if (listing.Price is not null) builder.AppendLine($"Price: {listing.Price} {listing.Currency}");
        builder.AppendLine("Use this only as a trend, tone, structure, and keyword reference. Do not copy its wording or assume its product facts apply to another product.");
        return builder.ToString();
    }
}

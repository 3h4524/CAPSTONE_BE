using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

public sealed class OpportunityService(
    ICurrentUser currentUser,
    IRepository<SellerResearchProfile> profiles,
    IRepository<MarketOpportunity> opportunities,
    IRepository<MarketListingSnapshot> snapshots,
    IRepository<MarketOpportunityEvidence> evidence,
    IRepository<SellerOpportunity> sellerOpportunities,
    IRepository<MarketQueryObservation> queryObservations,
    IMarketListingQueries listingQueries,
    IUnitOfWork unitOfWork) : IOpportunityService
{
    /// <summary>
    /// History read for the radar's market signals: long enough for the 28-day seasonality
    /// check and several weekly trend comparisons, without loading every snapshot ever taken.
    /// </summary>
    private const int MarketHistoryDays = 90;

    /// <summary>An opportunity card uses its niche's latest collection only if it is this recent.</summary>
    private const int NicheSignalDays = 7;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "saved", "ignored", "acted", "dismissed"
    };

    public async Task<SellerResearchProfileResponse> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var profile = (await profiles.FindAsync(x => x.UserId == userId, cancellationToken)).SingleOrDefault();
        return profile is null ? EmptyProfile(userId) : MapProfile(profile);
    }

    public async Task<SellerResearchProfileResponse> SaveProfileAsync(
        SellerResearchProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = RequireUserId();
        ValidatePriceRange(request.MinimumPrice, request.MaximumPrice);

        var existing = (await profiles.FindAsync(x => x.UserId == userId, cancellationToken)).SingleOrDefault();
        var now = DbNow();
        if (existing is null)
        {
            existing = new SellerResearchProfile { UserId = userId, ProfileVersion = 1, UpdatedAt = now };
            ApplyProfile(existing, request);
            await profiles.AddAsync(existing, cancellationToken: cancellationToken);
        }
        else
        {
            ApplyProfile(existing, request);
            existing.ProfileVersion++;
            existing.UpdatedAt = now;
            await profiles.UpdateAsync(existing, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapProfile(existing);
    }

    public async Task<IReadOnlyList<SellerNicheMarketInsightResponse>> GetMarketInsightsAsync(
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        limit = Math.Clamp(limit, 1, 50);
        var profile = (await profiles.FindAsync(x => x.UserId == userId, cancellationToken)).SingleOrDefault();
        var profileTerms = profile is null
            ? []
            : SellerMarketInsightCalculator.Tokenize(
                Read(profile.PreferredCategories)
                    .Concat(Read(profile.PreferredKeywords))
                    .Concat(Read(profile.TargetAudiences)));

        var historyStart = DbNow().AddDays(-MarketHistoryDays);
        var rows = await snapshots.Query()
            .Where(item => item.Platform == "etsy" && item.MatchedQuery != null && item.ObservedAt >= historyStart)
            .ToListAsync(cancellationToken);
        var supplyByQuery = await LoadSupplyAsync(DateOnly.FromDateTime(historyStart), cancellationToken);
        MarketSupplySignal SupplyFor(string query) =>
            supplyByQuery.GetValueOrDefault(MarketQueryKey.Normalize(query), MarketSupplySignal.Unknown);
        var marketScoreRows = await opportunities.Query()
            .Where(item => item.Platform == "etsy" && item.Status == "active" && !item.IsSynthetic)
            .Select(item => new { item.Niche, item.MarketOpportunityScore })
            .ToListAsync(cancellationToken);
        var marketScores = marketScoreRows
            .Where(item => !string.IsNullOrWhiteSpace(item.Niche))
            .GroupBy(item => item.Niche.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (double)group.Max(item => item.MarketOpportunityScore), StringComparer.OrdinalIgnoreCase);
        var matchedGroups = rows
            .GroupBy(item => item.MatchedQuery!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                var groupRows = group.ToArray();
                return new
                {
                    Query = group.Key,
                    Rows = groupRows,
                    Metrics = SellerMarketInsightCalculator.Calculate(group.Key, groupRows, profileTerms, SupplyFor(group.Key))
                };
            })
            .Where(item => profileTerms.Count == 0 || item.Metrics.RelevanceScore > 0)
            .OrderByDescending(item => item.Metrics.RelevanceScore)
            .ThenByDescending(item => item.Metrics.DistinctListingCount)
            .Take(limit)
            .ToArray();

        if (matchedGroups.Length == 0)
            return [];

        var matchedQueries = matchedGroups.Select(item => item.Query).ToArray();
        var matchedRows = matchedGroups
            .SelectMany(item => item.Rows)
            .GroupBy(item => new { ListingId = item.ExternalListingId.Trim(), item.ObservedAt })
            .Select(group => group
                .OrderByDescending(item => item.CollectedAt)
                .ThenByDescending(item => item.Id)
                .First())
            .ToArray();
        var relevanceScore = matchedGroups.Average(item => item.Metrics.RelevanceScore);
        var niche = BuildNicheLabel(profile, matchedQueries);
        var keywordInsights = matchedGroups
            .Select(item => SellerNicheInsightCalculator.CalculateKeyword(
                item.Query,
                item.Rows,
                marketScores.TryGetValue(item.Query, out var marketScore) ? marketScore : null,
                profileTerms,
                SupplyFor(item.Query)))
            .OrderByDescending(item => item.KeywordScore)
            .ThenBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nicheSupply = MarketSupplyCalculator.Combine(matchedQueries.Select(SupplyFor));
        return [SellerNicheInsightCalculator.Calculate(niche, matchedQueries, matchedRows, relevanceScore, keywordInsights, nicheSupply)];
    }

    public async Task<IReadOnlyList<SellerOpportunityResponse>> RefreshRankingAsync(CancellationToken cancellationToken = default)
    {
        await RankForUserAsync(RequireUserId(), cancellationToken);
        return await ListAsync(limit: 100, cancellationToken: cancellationToken);
    }

    public async Task<int> RefreshAllRankingsAsync(CancellationToken cancellationToken = default)
    {
        // Every seller with a research profile or a radar ranked before: each sees the day's collection on opening it.
        var withProfile = await profiles.Query().Select(x => x.UserId).ToListAsync(cancellationToken);
        var ranked = await sellerOpportunities.Query().Select(x => x.UserId).Distinct().ToListAsync(cancellationToken);
        var userIds = withProfile.Concat(ranked).Distinct().ToArray();
        foreach (var userId in userIds)
            await RankForUserAsync(userId, cancellationToken);
        return userIds.Length;
    }

    /// <summary>Scores every active opportunity for one seller and ranks the ones that qualify.</summary>
    private async Task RankForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = (await profiles.FindAsync(x => x.UserId == userId, cancellationToken)).SingleOrDefault();
        var snapshot = profile is null ? new SellerResearchProfileSnapshot([], [], [], []) : ToSnapshot(profile);
        // Demo opportunities (is_synthetic) link to listings that do not exist, so sellers are only
        // ranked against opportunities collected from the real marketplace.
        var activeRows = await opportunities.FindAsync(x => x.Status == "active" && !x.IsSynthetic, cancellationToken);
        var digitalIds = await listingQueries.GetDigitalOpportunityIdsAsync(activeRows.Select(x => x.Id).ToArray(), cancellationToken);
        var marketRows = activeRows.Where(x => !digitalIds.Contains(x.Id)).ToArray();
        var marketOpportunityIds = marketRows.Select(x => x.Id).ToArray();
        var latestPrices = await evidence.Query()
            .Where(x => marketOpportunityIds.Contains(x.OpportunityId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.OpportunityId, x.Snapshot.Price })
            .ToListAsync(cancellationToken);
        var priceByOpportunity = latestPrices
            .GroupBy(x => x.OpportunityId)
            .ToDictionary(group => group.Key, group => group.First().Price);
        var evidenceRows = await evidence.Query()
            .Where(x => marketOpportunityIds.Contains(x.OpportunityId))
            .Select(x => new
            {
                x.OpportunityId,
                x.Snapshot.ExternalListingId,
                x.Snapshot.ExternalSellerId,
                x.Snapshot.ObservedAt,
                x.Snapshot.IsSynthetic
            })
            .ToListAsync(cancellationToken);
        var evidenceStats = evidenceRows
            .GroupBy(x => x.OpportunityId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    EvidenceCount = group.Count(),
                    DistinctListingCount = group.Select(x => x.ExternalListingId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    DistinctSellerCount = group.Select(x => x.ExternalSellerId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ObservationPeriodCount = group.Select(x => x.ObservedAt.Date).Distinct().Count(),
                    RealEvidenceCount = group.Count(x => !x.IsSynthetic),
                    SyntheticEvidenceCount = group.Count(x => x.IsSynthetic)
                });
        var nicheEvidence = await LoadNicheEvidenceAsync(cancellationToken);
        var existingRows = await sellerOpportunities.FindAsync(x => x.UserId == userId, cancellationToken);
        var ranked = new List<SellerOpportunity>(marketRows.Length);

        foreach (var market in marketRows)
        {
            // An opportunity is one product concept with one to a few evidence listings; its market is
            // the niche it was collected for, sampled at 100 listings a day. Evidence quality therefore
            // counts the niche's listings and observed days, not only the concept's own evidence.
            var niche = nicheEvidence.GetValueOrDefault(MarketQueryKey.Normalize(market.Niche));
            var stats = evidenceStats.GetValueOrDefault(market.Id);
            var score = SellerOpportunityScorer.Score(
                new MarketOpportunityCandidate((double)market.MarketOpportunityScore, (double)market.Confidence,
                    MarketCategoryInference.Resolve(market), ExtractKeywords(market),
                    priceByOpportunity.GetValueOrDefault(market.Id),
                    stats?.EvidenceCount,
                    LargerOf(stats?.DistinctListingCount, niche?.ListingCount),
                    stats?.DistinctSellerCount,
                    LargerOf(stats?.ObservationPeriodCount, niche?.ObservedDayCount),
                    evidenceStats.GetValueOrDefault(market.Id)?.RealEvidenceCount,
                    evidenceStats.GetValueOrDefault(market.Id)?.SyntheticEvidenceCount,
                    market.LastObservedAt), snapshot);
            var row = existingRows.SingleOrDefault(x => x.OpportunityId == market.Id);
            if (row is null)
            {
                row = new SellerOpportunity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OpportunityId = market.Id,
                    State = "new",
                    FirstSeenAt = DbNow()
                };
                await sellerOpportunities.AddAsync(row, cancellationToken: cancellationToken);
            }

            row.SellerFitScore = (decimal)score.SellerFitScore;
            row.FinalScore = (decimal)score.FinalScore;
            row.FitExplanation = score.Explanation;
            row.FitBreakdown = JsonSerializer.Serialize(score.Breakdown, JsonOptions);
            row.LastEvaluatedAt = DbNow();
            row.Rank = null;
            if (existingRows.Contains(row))
                await sellerOpportunities.UpdateAsync(row, cancellationToken: cancellationToken);
            if (SellerOpportunityThresholds.IsQualified(score))
                ranked.Add(row);
        }

        foreach (var item in ranked.OrderByDescending(x => x.FinalScore).ThenBy(x => x.OpportunityId).Select((row, index) => (row, index)))
            item.row.Rank = item.index + 1;

        // An opportunity that went stale or was archived keeps its row (and the seller's state) but not its old rank.
        var activeIds = marketRows.Select(x => x.Id).ToHashSet();
        foreach (var row in existingRows.Where(x => x.Rank is not null && !activeIds.Contains(x.OpportunityId)))
        {
            row.Rank = null;
            await sellerOpportunities.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SellerOpportunityResponse>> ListAsync(
        string? state = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        limit = Math.Clamp(limit, 1, 100);
        // Rows ranked before demo data was excluded stay hidden until the next refresh replaces them.
        var query = sellerOpportunities.Query().Where(x => x.UserId == userId && !x.Opportunity.IsSynthetic);
        // An archived opportunity is a niche the marketplace moved on from: it leaves the radar unless the seller kept it.
        query = query.Where(x => x.Opportunity.Status != MarketOpportunityLifecycle.Archived || x.State == "saved" || x.State == "acted");
        query = query.Where(x =>
            x.SellerFitScore >= (decimal)SellerOpportunityThresholds.MinimumSellerFitScore
            && x.FinalScore >= (decimal)SellerOpportunityThresholds.MinimumFinalScore);
        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(x => x.State == state.Trim().ToLowerInvariant());

        var rows = await query
            .OrderBy(x => x.Rank ?? int.MaxValue)
            .ThenByDescending(x => x.FinalScore)
            .Include(x => x.Opportunity)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return [];

        // Rows ranked before digital downloads were excluded stay hidden; the next refresh drops them.
        var digitalIds = await listingQueries.GetDigitalOpportunityIdsAsync(rows.Select(row => row.OpportunityId).ToArray(), cancellationToken);
        rows = rows.Where(row => !digitalIds.Contains(row.OpportunityId)).ToList();
        if (rows.Count == 0) return [];

        var opportunityIds = rows.Select(row => row.OpportunityId).ToArray();
        var evidenceRows = await evidence.Query()
            .Where(item => opportunityIds.Contains(item.OpportunityId))
            .Select(item => new
            {
                item.OpportunityId,
                item.Snapshot.ExternalListingId,
                item.Snapshot.ExternalSellerId,
                item.Snapshot.Title,
                item.Snapshot.SourceUrl,
                item.Snapshot.Price,
                item.Snapshot.Currency,
                item.Snapshot.Favorites,
                item.Snapshot.Views,
                item.Snapshot.ObservedAt,
                item.Snapshot.IsSynthetic
            })
            .ToListAsync(cancellationToken);
        var insightsByOpportunity = evidenceRows
            .GroupBy(item => item.OpportunityId)
            .ToDictionary(
                group => group.Key,
                group => MarketOpportunityInsightCalculator.Calculate(
                    rows.Single(row => row.OpportunityId == group.Key).Opportunity,
                    group.Select(item => new MarketOpportunityEvidenceInput(
                        item.ExternalListingId,
                        item.ExternalSellerId,
                        item.Title,
                        item.SourceUrl,
                        item.Price,
                        item.Currency,
                        item.Favorites,
                        item.Views,
                        item.ObservedAt,
                        item.IsSynthetic)).ToArray()));

        var nicheMarkets = await LoadNicheMarketsAsync(rows.Select(row => row.Opportunity.Niche), cancellationToken);
        return rows.Select(row => MapOpportunity(
            row,
            insightsByOpportunity.GetValueOrDefault(
                row.OpportunityId,
                MarketOpportunityInsightCalculator.Calculate(row.Opportunity, [])),
            nicheMarkets.GetValueOrDefault(MarketQueryKey.Normalize(row.Opportunity.Niche)))).ToArray();
    }

    public async Task<SellerOpportunityResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var row = await sellerOpportunities.Query()
            .Where(x => x.Id == id && x.UserId == userId && !x.Opportunity.IsSynthetic)
            .Include(x => x.Opportunity)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        if ((await listingQueries.GetDigitalOpportunityIdsAsync([row.OpportunityId], cancellationToken)).Count > 0) return null;

        var evidenceRows = await evidence.Query()
            .Where(item => item.OpportunityId == row.OpportunityId)
            .Select(item => new
            {
                item.Snapshot.ExternalListingId,
                item.Snapshot.ExternalSellerId,
                item.Snapshot.Title,
                item.Snapshot.SourceUrl,
                item.Snapshot.Price,
                item.Snapshot.Currency,
                item.Snapshot.Favorites,
                item.Snapshot.Views,
                item.Snapshot.ObservedAt,
                item.Snapshot.IsSynthetic
            })
            .ToListAsync(cancellationToken);
        var insight = MarketOpportunityInsightCalculator.Calculate(
            row.Opportunity,
            evidenceRows.Select(item => new MarketOpportunityEvidenceInput(
                item.ExternalListingId,
                item.ExternalSellerId,
                item.Title,
                item.SourceUrl,
                item.Price,
                item.Currency,
                item.Favorites,
                item.Views,
                item.ObservedAt,
                item.IsSynthetic)).ToArray());
        var nicheMarkets = await LoadNicheMarketsAsync([row.Opportunity.Niche], cancellationToken);
        return MapOpportunity(row, insight, nicheMarkets.GetValueOrDefault(MarketQueryKey.Normalize(row.Opportunity.Niche)));
    }

    public async Task<SellerOpportunityResponse?> UpdateStateAsync(
        Guid id,
        string state,
        CancellationToken cancellationToken = default)
    {
        var normalizedState = state?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedState) || !AllowedStates.Contains(normalizedState))
            throw new ArgumentException("State must be one of: new, saved, ignored, acted, dismissed.", nameof(state));

        var userId = RequireUserId();
        var row = (await sellerOpportunities.FindAsync(x => x.Id == id && x.UserId == userId, cancellationToken)).SingleOrDefault();
        if (row is null) return null;
        row.State = normalizedState;
        if (normalizedState == "acted") row.ActedAt = DbNow();
        await sellerOpportunities.UpdateAsync(row, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private Guid RequireUserId() => currentUser.UserId ?? throw new UnauthorizedAccessException("Authentication is required.");

    private static void ValidatePriceRange(decimal? minimum, decimal? maximum)
    {
        if (minimum is < 0 || maximum is < 0 || (minimum is not null && maximum is not null && minimum > maximum))
            throw new ArgumentException("Price range is invalid.");
    }

    private static void ApplyProfile(SellerResearchProfile entity, SellerResearchProfileRequest request)
    {
        entity.ShopGoal = Clean(request.ShopGoal);
        entity.TargetAudiences = Serialize(request.TargetAudiences);
        entity.PreferredCategories = Serialize(request.PreferredCategories);
        entity.ExcludedCategories = Serialize(request.ExcludedCategories);
        entity.PreferredKeywords = Serialize(request.PreferredKeywords);
        entity.MinimumPrice = request.MinimumPrice;
        entity.MaximumPrice = request.MaximumPrice;
    }

    private static SellerResearchProfileSnapshot ToSnapshot(SellerResearchProfile profile) =>
        new(Read(profile.PreferredCategories), Read(profile.PreferredKeywords), Read(profile.PreferredCategories),
            Read(profile.ExcludedCategories), profile.MinimumPrice, profile.MaximumPrice);

    private static SellerResearchProfileResponse MapProfile(SellerResearchProfile profile) =>
        new(profile.UserId, profile.ShopGoal, Read(profile.TargetAudiences), Read(profile.PreferredCategories),
            Read(profile.ExcludedCategories), Read(profile.PreferredKeywords), profile.MinimumPrice,
            profile.MaximumPrice, profile.ProfileVersion, profile.UpdatedAt);

    private static SellerResearchProfileResponse EmptyProfile(Guid userId) =>
        new(userId, null, [], [], [], [], null, null, 0, DbNow());

    /// <summary>Supply of every tracked query, ranked by size so competition is relative to the other niches.</summary>
    private async Task<IReadOnlyDictionary<string, MarketSupplySignal>> LoadSupplyAsync(
        DateOnly since,
        CancellationToken cancellationToken)
    {
        var observationRows = await queryObservations.Query()
            .Where(item => item.Platform == "etsy" && item.ObservedOn >= since)
            .ToListAsync(cancellationToken);
        var supplyByQuery = observationRows
            .GroupBy(item => item.Query, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => MarketSupplyCalculator.Calculate(group.ToArray()),
                StringComparer.Ordinal);
        return MarketSupplyCalculator.RankSizes(supplyByQuery);
    }

    /// <summary>
    /// Market signals of the niches behind the given opportunities, from each niche's latest
    /// collection within <see cref="NicheSignalDays"/> days. A niche without any data is left out.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, OpportunityNicheMarket>> LoadNicheMarketsAsync(
        IEnumerable<string?> niches,
        CancellationToken cancellationToken)
    {
        var keys = niches
            .Select(MarketQueryKey.Normalize)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
            return new Dictionary<string, OpportunityNicheMarket>(StringComparer.Ordinal);

        var now = DbNow();
        var supplyByQuery = await LoadSupplyAsync(DateOnly.FromDateTime(now.AddDays(-MarketHistoryDays)), cancellationToken);
        var since = now.AddDays(-NicheSignalDays);
        var rows = await snapshots.Query()
            .Where(item => item.Platform == "etsy"
                && item.MatchedQuery != null
                && item.ObservedAt >= since
                && keys.Contains(item.MatchedQuery.ToLower()))
            .ToListAsync(cancellationToken);
        var signalsByKey = rows
            .GroupBy(item => MarketQueryKey.Normalize(item.MatchedQuery), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => MarketListingSignalsCalculator.Calculate(MarketListingFilter.Apply(group.ToArray()).Eligible),
                StringComparer.Ordinal);

        var result = new Dictionary<string, OpportunityNicheMarket>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var supply = supplyByQuery.GetValueOrDefault(key, MarketSupplySignal.Unknown);
            var signals = signalsByKey.GetValueOrDefault(key, MarketListingSignals.Empty);
            if (supply.ActiveListingCount is null && signals.SampleListingCount == 0)
                continue;

            result[key] = new OpportunityNicheMarket(
                key,
                supply.ActiveListingCount,
                supply.SizePercentile,
                MarketSupplyCalculator.Competition(supply.SizePercentile),
                SellerMarketInsightCalculator.DemandProxy(signals),
                signals.MedianFavoritesPer30Days,
                signals.SellerCount,
                signals.StrongSellerCount,
                signals.RisingNewListingCount,
                supply.LatestObservedOn ?? signals.LatestCollectedOn);
        }

        return result;
    }

    private static SellerOpportunityResponse MapOpportunity(
        SellerOpportunity row,
        MarketOpportunityInsights insights,
        OpportunityNicheMarket? nicheMarket)
    {
        var opportunity = row.Opportunity;
        var breakdown = JsonSerializer.Deserialize<Dictionary<string, double>>(row.FitBreakdown, JsonOptions) ?? [];
        return new(row.Id, row.OpportunityId, opportunity.Platform, opportunity.Title, opportunity.Niche,
            MarketCategoryInference.Resolve(opportunity), opportunity.Summary, opportunity.MarketOpportunityScore, opportunity.Confidence,
            row.SellerFitScore, row.FinalScore, row.Rank, row.State, row.FitExplanation, breakdown, row.LastEvaluatedAt, insights, nicheMarket);
    }

    private static IReadOnlyCollection<string> ExtractKeywords(MarketOpportunity opportunity) =>
        $"{opportunity.Niche} {opportunity.Title} {MarketCategoryInference.Resolve(opportunity)}".Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? LargerOf(int? first, int? second) =>
        first is null && second is null ? null : Math.Max(first ?? 0, second ?? 0);

    private sealed record NicheEvidence(int ListingCount, int ObservedDayCount);

    /// <summary>Distinct real listings and observed days per collected niche over the market history window.</summary>
    private async Task<Dictionary<string, NicheEvidence>> LoadNicheEvidenceAsync(CancellationToken cancellationToken)
    {
        var since = DbNow().AddDays(-MarketHistoryDays);
        var rows = await snapshots.Query()
            .Where(item => item.Platform == "etsy" && !item.IsSynthetic && item.MatchedQuery != null && item.ObservedAt >= since)
            .GroupBy(item => item.MatchedQuery!.ToLower())
            .Select(group => new
            {
                Query = group.Key,
                ListingCount = group.Select(item => item.ExternalListingId).Distinct().Count(),
                ObservedDayCount = group.Select(item => item.ObservedAt.Date).Distinct().Count()
            })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => MarketQueryKey.Normalize(row.Query), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new NicheEvidence(group.Max(row => row.ListingCount), group.Max(row => row.ObservedDayCount)),
                StringComparer.Ordinal);
    }

    private static string BuildNicheLabel(SellerResearchProfile? profile, IReadOnlyList<string> matchedQueries)
    {
        var profileLabels = profile is null
            ? []
            : Read(profile.PreferredCategories)
                .Concat(Read(profile.PreferredKeywords))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)
                .ToArray();

        return profileLabels.Length > 0
            ? string.Join(" · ", profileLabels)
            : matchedQueries.FirstOrDefault() ?? "Seller niche";
    }

    private static string Serialize(IEnumerable<string>? values) =>
        JsonSerializer.Serialize((values ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);

    private static IReadOnlyList<string> Read(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<string[]>(value, JsonOptions) ?? [];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}

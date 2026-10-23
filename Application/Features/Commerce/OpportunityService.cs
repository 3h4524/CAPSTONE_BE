using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

public sealed class OpportunityService(
    ICurrentUser currentUser,
    IRepository<SellerResearchProfile> profiles,
    IRepository<MarketOpportunity> opportunities,
    IRepository<SellerOpportunity> sellerOpportunities,
    IUnitOfWork unitOfWork) : IOpportunityService
{
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

    public async Task<IReadOnlyList<SellerOpportunityResponse>> RefreshRankingAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var profile = (await profiles.FindAsync(x => x.UserId == userId, cancellationToken)).SingleOrDefault();
        var snapshot = profile is null ? new SellerResearchProfileSnapshot([], [], [], []) : ToSnapshot(profile);
        var marketRows = await opportunities.FindAsync(x => x.Status == "active", cancellationToken);
        var existingRows = await sellerOpportunities.FindAsync(x => x.UserId == userId, cancellationToken);
        var ranked = new List<SellerOpportunity>(marketRows.Count);

        foreach (var market in marketRows)
        {
            var score = SellerOpportunityScorer.Score(
                new MarketOpportunityCandidate((double)market.MarketOpportunityScore, (double)market.Confidence,
                    market.Category, ExtractKeywords(market), null), snapshot);
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
            if (existingRows.Contains(row))
                await sellerOpportunities.UpdateAsync(row, cancellationToken: cancellationToken);
            ranked.Add(row);
        }

        foreach (var item in ranked.OrderByDescending(x => x.FinalScore).ThenBy(x => x.OpportunityId).Select((row, index) => (row, index)))
            item.row.Rank = item.index + 1;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListAsync(limit: 100, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SellerOpportunityResponse>> ListAsync(
        string? state = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        limit = Math.Clamp(limit, 1, 100);
        var query = sellerOpportunities.Query().Where(x => x.UserId == userId);
        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(x => x.State == state.Trim().ToLowerInvariant());

        var rows = await query
            .OrderBy(x => x.Rank ?? int.MaxValue)
            .ThenByDescending(x => x.FinalScore)
            .Include(x => x.Opportunity)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(MapOpportunity).ToArray();
    }

    public async Task<SellerOpportunityResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var row = await sellerOpportunities.Query()
            .Where(x => x.Id == id && x.UserId == userId)
            .Include(x => x.Opportunity)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : MapOpportunity(row);
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

    private static SellerOpportunityResponse MapOpportunity(SellerOpportunity row)
    {
        var opportunity = row.Opportunity;
        var breakdown = JsonSerializer.Deserialize<Dictionary<string, double>>(row.FitBreakdown, JsonOptions) ?? [];
        return new(row.Id, row.OpportunityId, opportunity.Platform, opportunity.Title, opportunity.Niche,
            opportunity.Category, opportunity.Summary, opportunity.MarketOpportunityScore, opportunity.Confidence,
            row.SellerFitScore, row.FinalScore, row.Rank, row.State, row.FitExplanation, breakdown, row.LastEvaluatedAt);
    }

    private static IReadOnlyCollection<string> ExtractKeywords(MarketOpportunity opportunity) =>
        $"{opportunity.Niche} {opportunity.Title} {opportunity.Category}".Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Serialize(IEnumerable<string>? values) =>
        JsonSerializer.Serialize((values ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);

    private static IReadOnlyList<string> Read(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<string[]>(value, JsonOptions) ?? [];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}

using APCS.Application.Features.SellerDashboard.Dtos.Response;

namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>How the products of one niche did over the period.</summary>
public sealed record NicheOutcome(string Niche, int Total, int Failed);

/// <summary>
/// Reads the period's figures for what the Seller would want pointed out: a niche that works, one that
/// keeps failing, the usual cause of failures, and a success rate or AI cost moving the wrong way.
/// Each rule needs enough data to mean something, so a quiet period says nothing rather than guess.
/// </summary>
public static class DashboardInsights
{
    public const int MaxInsights = 3;
    public const int MinNicheProducts = 5;
    public const decimal NicheFailureWarningPercent = 25;
    public const int MinFailuresForReason = 2;
    public const decimal SuccessDropPoints = 10;
    public const decimal CostChangePercent = 20;

    public const string Good = "good";
    public const string Warning = "warning";

    public static IReadOnlyList<InsightDto> Build(
        DashboardKpisDto kpis,
        IReadOnlyList<NicheOutcome> niches,
        (string Message, int Count, int TotalFailed)? topFailure)
    {
        var insights = new List<InsightDto>();

        var success = kpis.SuccessRatePercent;
        if (success.PreviousValue > 0 && success.PreviousValue - success.Value >= SuccessDropPoints)
            insights.Add(new InsightDto(Warning,
                $"Success rate fell from {success.PreviousValue:0.#}% to {success.Value:0.#}%",
                "More products failed than in the period before. Check what made them fail."));

        if (topFailure is { } failure && failure.Count >= MinFailuresForReason)
            insights.Add(new InsightDto(Warning,
                $"Most failures: \"{Shorten(failure.Message)}\"",
                $"{failure.Count} of the {failure.TotalFailed} failed products stopped for this reason."));

        var rated = niches.Where(niche => niche.Total >= MinNicheProducts)
            .Select(niche => (niche.Niche, niche.Total, FailurePercent: (decimal)niche.Failed / niche.Total * 100))
            .ToList();
        var worst = rated.OrderByDescending(niche => niche.FailurePercent).FirstOrDefault();
        var isWorstFlagged = worst != default && worst.FailurePercent >= NicheFailureWarningPercent;
        if (isWorstFlagged)
            insights.Add(new InsightDto(Warning,
                $"{worst.Niche} fails often ({worst.FailurePercent:0}%)",
                $"{worst.FailurePercent:0}% of your {worst.Total} {worst.Niche} products failed. Try another style or prompt for it."));

        var cost = kpis.CostPerProductUsd;
        if (cost.ChangePercent is >= CostChangePercent)
            insights.Add(new InsightDto(Warning,
                $"AI cost per product rose {cost.ChangePercent:0}%",
                $"Each product now costs about ${cost.Value:0.###} in AI calls, up from ${cost.PreviousValue:0.###}."));

        var best = rated.Where(niche => !isWorstFlagged || niche.Niche != worst.Niche)
            .OrderBy(niche => niche.FailurePercent).ThenByDescending(niche => niche.Total)
            .FirstOrDefault();
        if (best != default && best.FailurePercent < NicheFailureWarningPercent)
            insights.Add(new InsightDto(Good,
                $"{best.Niche} is your most reliable niche",
                $"{100 - best.FailurePercent:0}% of your {best.Total} {best.Niche} products made it through. Worth making more."));

        if (cost.ChangePercent is <= -CostChangePercent)
            insights.Add(new InsightDto(Good,
                $"AI cost per product fell {-cost.ChangePercent:0}%",
                $"Each product now costs about ${cost.Value:0.###} in AI calls, down from ${cost.PreviousValue:0.###}."));

        return insights.Take(MaxInsights).ToList();
    }

    private static string Shorten(string message) =>
        message.Length <= 60 ? message.Trim() : message[..57].TrimEnd() + "…";
}

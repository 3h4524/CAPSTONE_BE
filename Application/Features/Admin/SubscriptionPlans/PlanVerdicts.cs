using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>One Seller's use of the two core features in the period.</summary>
internal sealed record SellerUsage(int DesignImages, int Mockups);

/// <summary>What <see cref="PlanVerdicts"/> needs to judge one paid plan.</summary>
/// <param name="RetentionStart">Customers on the plan at the period's start, counted like MRR.</param>
/// <param name="Retained">Of those, the customers still paying at its end, on any plan.</param>
/// <param name="RetentionRate">Retained / RetentionStart in percent; null when too few to judge.</param>
/// <param name="OnSale">Whether Sellers can buy the plan now, so it can be offered as an upgrade.</param>
internal sealed record PlanVerdictInput(
    string Key,
    string Name,
    bool OnSale,
    int Subscribers,
    bool HasEnoughData,
    decimal MonthlyPriceVnd,
    IReadOnlyList<FeatureAdoptionDto> Adoption,
    IReadOnlyList<SellerUsage> Sellers,
    int RetentionStart,
    int Retained,
    decimal? RetentionRate);

/// <summary>
/// Turns each paid plan's figures into one verdict, the evidence behind it and the action it
/// suggests, by fixed rules so the same data always reads the same way.
/// </summary>
/// <remarks>
/// Plans are compared with their neighbours by price. In order of urgency: a plan that costs far
/// more than the one below while its Sellers use no more is overpriced; the plan that keeps the
/// fewest of its customers is a retention risk; a plan whose Sellers already use as much as a
/// typical Seller on a plan above has upsell candidates. That plan must be on sale and used more
/// than this one, so no Seller is pointed at a plan that gives them nothing more. A plan that meets
/// none of these is healthy.
/// </remarks>
internal static class PlanVerdicts
{
    /// <summary>Judges every plan with the thresholds in <paramref name="rules"/>.</summary>
    public static IReadOnlyList<PlanVerdictDto> Build(IReadOnlyList<PlanVerdictInput> plans, PlanVerdictOptions rules)
    {
        var ordered = plans.OrderBy(plan => plan.MonthlyPriceVnd).ToList();
        var lowestRetention = ordered
            .Where(plan => plan.HasEnoughData && plan.RetentionRate is not null)
            .MinBy(plan => plan.RetentionRate);

        return ordered
            .Select((plan, index) => plan.HasEnoughData
                ? Judge(plan, index > 0 ? ordered[index - 1] : null, UpgradeFor(plan, ordered.Skip(index + 1)), lowestRetention, rules)
                : new PlanVerdictDto(plan.Key, plan.Name, "not_enough_data", "Not enough data", "neutral",
                    [$"{plan.Subscribers} subscriber{(plan.Subscribers == 1 ? "" : "s")}, at least {rules.MinimumSample} needed to judge."],
                    null, 0))
            .ToList();
    }

    private static PlanVerdictDto Judge(
        PlanVerdictInput plan,
        PlanVerdictInput? below,
        PlanVerdictInput? above,
        PlanVerdictInput? lowestRetention,
        PlanVerdictOptions rules)
    {
        var designs = Median(plan.Sellers.Select(seller => seller.DesignImages));
        var mockups = Median(plan.Sellers.Select(seller => seller.Mockups));
        var evidence = new List<string>();
        (string Code, string Label, string Tone, string Action)? verdict = null;

        if (below is { HasEnoughData: true } && below.MonthlyPriceVnd > 0
            && plan.MonthlyPriceVnd >= below.MonthlyPriceVnd * rules.PremiumPriceRatio)
        {
            var belowDesigns = Median(below.Sellers.Select(seller => seller.DesignImages));
            var belowMockups = Median(below.Sellers.Select(seller => seller.Mockups));
            if (designs <= belowDesigns && mockups <= belowMockups)
            {
                var ratio = Math.Round(plan.MonthlyPriceVnd / below.MonthlyPriceVnd, 1);
                evidence.Add(FormattableString.Invariant($"Pays {ratio:0.#}× {below.Name}, but a typical Seller makes {designs:0.#} designs and {mockups:0.#} mockups against {below.Name}'s {belowDesigns:0.#} and {belowMockups:0.#}."));
                verdict = ("overpriced", $"Overpriced vs {below.Name}", "warning",
                    $"Find out which Sellers need {plan.Name}'s higher limits. The rest get little more than {below.Name} for their money, so add value or expect downgrades.");
            }
        }

        if (plan.RetentionRate is { } rate && lowestRetention == plan && rate < rules.LowRetentionRate)
        {
            evidence.Add(FormattableString.Invariant($"Keeps {rate:0.#}% of its customers ({plan.Retained} of {plan.RetentionStart}), the lowest of all plans."));
            verdict ??= ("retention_risk", "Retention risk", "danger",
                $"Find out why {plan.Name} customers leave: ask the ones who stopped, and show new ones the features the ones who stay use.");
        }
        else if (plan.RetentionRate is { } kept)
        {
            evidence.Add(FormattableString.Invariant($"Keeps {kept:0.#}% of its customers ({plan.Retained} of {plan.RetentionStart})."));
        }

        if (above is not null)
        {
            var aboveDesigns = Median(above.Sellers.Select(seller => seller.DesignImages));
            var aboveMockups = Median(above.Sellers.Select(seller => seller.Mockups));
            var candidates = plan.Sellers.Count(seller => seller.DesignImages >= aboveDesigns && seller.Mockups >= aboveMockups);
            if (candidates > 0)
            {
                evidence.Add(FormattableString.Invariant($"{candidates} Seller{(candidates == 1 ? " already makes" : "s already make")} as many designs and mockups as a typical {above.Name} Seller ({aboveDesigns:0.#} designs, {aboveMockups:0.#} mockups)."));
                verdict ??= ("upsell", "Upsell ready", "info", $"Offer {above.Name} to the {candidates} heaviest {plan.Name} Seller{(candidates == 1 ? "" : "s")}.");
                return Finish(plan, verdict.Value, evidence, candidates, above.Name);
            }
        }

        var unused = plan.Adoption
            .Where(feature => feature.IncludedInPlan && feature.Rate < rules.PromoteBelowRate)
            .MinBy(feature => feature.Rate);
        if (unused is not null)
        {
            evidence.Add(FormattableString.Invariant($"{unused.Label} is used by only {unused.Rate:0.#}% of its Sellers."));
        }

        verdict ??= ("healthy", "Healthy", "success",
            unused is not null ? $"Show {unused.Label} to Sellers who have not tried it." : "Nothing to change now.");
        return Finish(plan, verdict.Value, evidence, 0, above?.Name);
    }

    // The cheapest plan above that is on sale, judged, and whose typical Seller uses more than this
    // plan's does; a plan used no more is no upgrade.
    private static PlanVerdictInput? UpgradeFor(PlanVerdictInput plan, IEnumerable<PlanVerdictInput> above)
    {
        var designs = Median(plan.Sellers.Select(seller => seller.DesignImages));
        var mockups = Median(plan.Sellers.Select(seller => seller.Mockups));
        return above.FirstOrDefault(candidate =>
        {
            if (!candidate.OnSale || !candidate.HasEnoughData) return false;
            var candidateDesigns = Median(candidate.Sellers.Select(seller => seller.DesignImages));
            var candidateMockups = Median(candidate.Sellers.Select(seller => seller.Mockups));
            return candidateDesigns >= designs && candidateMockups >= mockups
                && (candidateDesigns > designs || candidateMockups > mockups);
        });
    }

    private static PlanVerdictDto Finish(
        PlanVerdictInput plan,
        (string Code, string Label, string Tone, string Action) verdict,
        List<string> evidence,
        int upsellCandidates,
        string? upsellPlan) =>
        new(plan.Key, plan.Name, verdict.Code, verdict.Label, verdict.Tone, evidence.Take(3).ToList(), verdict.Action, upsellCandidates,
            upsellPlan,
            Median(plan.Sellers.Select(seller => seller.DesignImages)),
            Median(plan.Sellers.Select(seller => seller.Mockups)));

    // Nearest-rank, like the "typical" figure the usage card shows, so the two always agree.
    internal static decimal Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        if (sorted.Count == 0) return 0m;
        var rank = (int)Math.Ceiling(0.5m * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}

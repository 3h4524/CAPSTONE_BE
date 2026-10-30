namespace APCS.Application.Features.Entitlements;

/// <summary>
/// What a Seller's current plan allows. A Seller with no plan in effect is on Free: they can run the
/// basic workflow within Free's monthly allowances (see <see cref="UsageAllowanceOptions"/>).
/// </summary>
/// <param name="HasPlan">False on Free: no active subscription, or a trial that has ended.</param>
/// <param name="IsTrial">True while the Seller is on the free Pro trial.</param>
/// <param name="MaxBatchSize">Products per run; -1 means no limit.</param>
/// <param name="MaxConcurrentJobs">Design jobs running at once; -1 means no limit.</param>
/// <param name="MaxMockupsPerRun">Mockups one run may make; -1 means no limit.</param>
public sealed record PlanEntitlements(
    bool HasPlan,
    Guid? PlanId,
    string? PlanName,
    string? Tier,
    bool IsTrial,
    DateTime? TrialEndsAt,
    int MaxBatchSize,
    int MaxConcurrentJobs,
    bool PrioritySupport,
    IReadOnlyDictionary<string, PlanFeatureValue> Features,
    int MaxMockupsPerRun = -1)
{
    /// <summary>Products in one Free run.</summary>
    public const int FreeProductsPerRun = 1;

    /// <summary>Design variations per product on Free.</summary>
    public const int FreeVariations = 1;

    /// <summary>Mockups one Free run may make.</summary>
    public const int FreeMockupsPerRun = 3;

    /// <summary>
    /// The entitlements of a Seller with no plan in effect: one product and one design variation per
    /// run, standard videos only, and listings within the monthly allowance. No daily caps apply:
    /// the monthly allowances bound everything Free does.
    /// </summary>
    public static PlanEntitlements Free { get; } = new(
        false, null, null, null, false, null, FreeProductsPerRun, 1, false,
        new Dictionary<string, PlanFeatureValue>
        {
            [PlanFeatures.DesignVariations] = new(true, FreeVariations),
            [PlanFeatures.AutoApproval] = new(true, null),
            [PlanFeatures.WorkflowListing] = new(true, null),
        },
        FreeMockupsPerRun);

    /// <summary>True on Free, where the monthly allowances apply.</summary>
    public bool IsFree => !HasPlan;

    /// <summary>Checks whether a flag feature is switched on.</summary>
    public bool IsEnabled(string code) => Features.TryGetValue(code, out var value) && value.IsEnabled;

    /// <summary>Gets a limit feature's value: null when none is set, 0 when the feature is off.</summary>
    public int? Limit(string code)
    {
        if (!Features.TryGetValue(code, out var value)) return null;
        return value.IsEnabled ? value.LimitValue : 0;
    }
}

/// <summary>One plan feature's setting.</summary>
public sealed record PlanFeatureValue(bool IsEnabled, int? LimitValue);

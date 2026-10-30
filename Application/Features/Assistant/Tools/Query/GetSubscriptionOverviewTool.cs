using APCS.Application.Features.Subscriptions;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class GetSubscriptionOverviewTool(ISubscriptionService subscriptions) : AssistantTool<NoArguments>
{
    public override string Name => "get_subscription_overview";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "Get the user's current subscription plan, quotas and renewal date, plus the plans available to buy with their prices and limits.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken)
    {
        var result = await subscriptions.GetOverviewAsync(cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        var overview = result.Value;
        return AssistantToolOutcome.Ok(new
        {
            plan = overview.CurrentSubscription is null ? null : new
            {
                name = overview.CurrentSubscription.PlanName,
                status = overview.CurrentSubscription.Status,
                billingCycle = overview.CurrentSubscription.BillingCycle,
                price = overview.CurrentSubscription.Price,
                startDate = overview.CurrentSubscription.StartDate,
                renewalDate = overview.CurrentSubscription.RenewalDate,
                scheduledPlan = overview.CurrentSubscription.ScheduledPlanName,
                scheduledPlanEffectiveDate = overview.CurrentSubscription.ScheduledPlanEffectiveDate
            },
            quotas = overview.UsageQuotas.Select(quota => new
            {
                quota.QuotaCode,
                quota.Label,
                quota.Used,
                quota.Limit,
                quota.Unit,
                quota.PercentUsed,
                quota.IsWarning
            }),
            availablePlans = overview.AvailablePlans.Select(plan => new
            {
                plan.Name,
                plan.Tier,
                plan.Description,
                plan.MonthlyPriceUsd,
                plan.AnnualPriceUsd,
                plan.IsCurrentPlan,
                plan.Quotas
            }),
            hasActivePaidPlan = overview.HasActivePaidPlan
        });
    }
}

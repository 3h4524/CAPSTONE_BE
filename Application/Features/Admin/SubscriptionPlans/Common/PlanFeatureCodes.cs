namespace APCS.Application.Features.Admin.SubscriptionPlans.Common;

/// <summary>
/// <c>plan_features.feature_code</c> values the Admin Portal writes. Feature codes are otherwise
/// free-form, database-owned data (see <c>PlanFeatureFlagDto</c> remarks in the Subscriptions
/// feature) — this is the one code the application itself defines and depends on, because
/// "White-label export" (ADM-05) has no dedicated column on <c>subscription_plans</c>.
/// </summary>
internal static class PlanFeatureCodes
{
    public const string WhiteLabelExport = "white_label_export";
}

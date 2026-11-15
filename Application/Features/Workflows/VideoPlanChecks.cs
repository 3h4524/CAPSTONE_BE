using APCS.Application.Features.Entitlements;
using APCS.Common.Models;

namespace APCS.Application.Features.Workflows;

public static class VideoPlanChecks
{
    /// <summary>
    /// Checks a video configuration against the Seller's plan: AI video and landscape output are plan features, and
    /// standard renders count toward Free's or the trial's allowance and the daily safety cap.
    /// </summary>
    /// <param name="requested">Videos about to be made with it: one per design of a batch run.</param>
    public static async Task<Result> CheckAsync(IPlanEntitlementService entitlements, Guid userId, GenerateVideoConfig config,
        int requested, CancellationToken ct)
    {
        if (config.Mode == "ai_shot") return await entitlements.RequireFeatureAsync(userId, PlanFeatures.AiVideo, ct);
        var features = await CheckFeaturesAsync(entitlements, userId, config, ct);
        if (features.IsFailure) return features;
        var allowance = await entitlements.RequireAllowanceAsync(userId, UsageAllowances.Videos, requested, ct);
        if (allowance.IsFailure) return allowance;
        return await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyVideoCap, requested, ct);
    }

    /// <summary>
    /// The plan features a configuration needs, without counting videos: for the settings of one design of a batch run,
    /// whose videos are counted once for the whole run.
    /// </summary>
    public static async Task<Result> CheckFeaturesAsync(IPlanEntitlementService entitlements, Guid userId, GenerateVideoConfig config,
        CancellationToken ct)
    {
        if (config.Mode == "ai_shot") return await entitlements.RequireFeatureAsync(userId, PlanFeatures.AiVideo, ct);
        var standard = await entitlements.RequireFeatureAsync(userId, PlanFeatures.StandardVideo, ct);
        if (standard.IsFailure) return standard;
        if (config.OutputFormat == "landscape")
        {
            var format = await entitlements.RequireFeatureAsync(userId, PlanFeatures.AllVideoFormats, ct);
            if (format.IsFailure) return format;
        }
        return Result.Success();
    }
}

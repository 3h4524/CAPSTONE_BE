namespace APCS.Application.Features.Entitlements;

/// <summary>The things Free and the Pro trial are counted on, with the words the client shows.</summary>
public static class UsageAllowances
{
    public const string FullRuns = "full_runs";
    public const string DesignRuns = "design_runs";
    public const string DesignImages = "design_images";
    public const string Products = "products";
    public const string Mockups = "mockups";
    public const string Videos = "videos";
    public const string Listings = "listings";

    public const string ScopeFree = "free";
    public const string ScopeTrial = "trial";

    /// <summary>A paid plan with a monthly limit, such as Starter's listings per month.</summary>
    public const string ScopePlan = "plan";

    public static string Label(string code) => code switch
    {
        FullRuns => "full runs",
        DesignRuns => "design runs",
        DesignImages => "design images",
        Products => "products",
        Mockups => "mockups",
        Videos => "videos",
        Listings => "listings",
        _ => code,
    };
}

/// <summary>What a Seller on Free or the Pro trial has used, and what they may use.</summary>
/// <param name="Scope">"free", "trial", "plan" on a paid plan with a monthly limit, or null when nothing is counted.</param>
/// <param name="ResetsOn">Free and a paid plan: the 1st of next month. Trial: the day it ends.</param>
/// <param name="MockupsPerRun">Mockups one run may make; null when there is no such limit.</param>
public sealed record UsageAllowanceSummary(
    string? Scope,
    DateOnly? ResetsOn,
    int? MockupsPerRun,
    IReadOnlyList<UsageAllowanceItem> Items);

/// <param name="Used">How many were used in the current month or trial.</param>
public sealed record UsageAllowanceItem(string Code, string Label, int Used, int Limit);

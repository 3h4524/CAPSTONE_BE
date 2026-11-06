namespace APCS.Application.Features.Entitlements;

/// <summary>
/// How much Free and the Pro trial may use, bound from the "UsageAllowances" configuration section.
/// Free counts each calendar month (UTC) from the 1st; the trial counts its whole 14 days. Paid
/// plans have no allowances: their daily safety caps apply instead.
/// </summary>
public sealed class UsageAllowanceOptions
{
    public const string SectionName = "UsageAllowances";

    public FreeAllowances Free { get; set; } = new();

    public TrialAllowances Trial { get; set; } = new();

    public bool IsValid() =>
        Free.FullRuns >= 0 && Free.DesignRuns >= 0 && Free.DesignImages >= 0
        && Free.Mockups >= 0 && Free.Videos >= 0 && Free.Listings >= 0
        && Trial.ProductsPerRun >= 1 && Trial.Products >= 0 && Trial.DesignImages >= 0
        && Trial.Mockups >= 0 && Trial.Videos >= 0 && Trial.Listings >= 0;
}

/// <summary>Free's monthly allowances.</summary>
public sealed class FreeAllowances
{
    /// <summary>Workflow runs that make a video.</summary>
    public int FullRuns { get; set; } = 2;

    /// <summary>Design generations started, for a full run or on their own.</summary>
    public int DesignRuns { get; set; } = 5;

    /// <summary>Design images made, regenerations included.</summary>
    public int DesignImages { get; set; } = 7;

    public int Mockups { get; set; } = 6;

    public int Videos { get; set; } = 2;

    public int Listings { get; set; } = 5;
}

/// <summary>The Pro trial's allowances for its whole length.</summary>
public sealed class TrialAllowances
{
    public int ProductsPerRun { get; set; } = 3;

    public int Products { get; set; } = 10;

    public int DesignImages { get; set; } = 40;

    public int Mockups { get; set; } = 100;

    public int Videos { get; set; } = 10;

    public int Listings { get; set; } = 20;
}

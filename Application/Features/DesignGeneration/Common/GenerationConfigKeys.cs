namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>
/// Keys this feature reads/writes inside the shared <c>BatchJob.Config</c> JSON blob. Other features
/// (e.g. <c>MockupTemplateService</c>) write their own keys into the same blob — always merge, never
/// overwrite the whole object.
/// </summary>
public static class GenerationConfigKeys
{
    public const string VariationCount = "variationCount";
    public const string AspectRatio = "aspectRatio";
    public const string Instructions = "instructions";

    /// <summary>
    /// Whether the job's designs need the Seller's approval before mock-ups are made. Absent on jobs started before
    /// approval existed, which keep the old behavior (one design per product, no review).
    /// </summary>
    public const string RequireApproval = "requireApproval";

    public const int DefaultVariationCount = 1;
    public const string DefaultAspectRatio = "1:1";
}

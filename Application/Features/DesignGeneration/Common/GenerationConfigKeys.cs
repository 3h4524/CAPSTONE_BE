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

    public const int DefaultVariationCount = 1;
    public const string DefaultAspectRatio = "1:1";
}

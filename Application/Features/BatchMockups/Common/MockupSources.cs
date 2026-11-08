namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>
/// Where a mock-up image came from, and what a composited one carries from the start so it can
/// later be used as a source of the product's video.
/// </summary>
public static class MockupSources
{
    /// <summary>Composited here from a design and a mock-up template.</summary>
    public const string Generated = "generated";

    /// <summary>Uploaded by the user as a finished mock-up photo.</summary>
    public const string Uploaded = "uploaded";

    /// <summary>Generated from an existing product image to prepare an AI video start frame.</summary>
    public const string AiGenerated = "ai_generated";

    public const string DefaultRole = "Hero";

    /// <summary>A mock-up of another side of the product, such as its back.</summary>
    public const string AlternativeAngleRole = "AlternativeAngle";

    /// <summary>The role of a mock-up made on a template photo showing this side of the garment.</summary>
    public static string RoleForView(string view) =>
        view == MockupRules.ViewFront ? DefaultRole : AlternativeAngleRole;

    /// <summary>No protected regions marked.</summary>
    public const string NoRegions = "{}";

    /// <summary>The first revision of a mock-up's metadata.</summary>
    public const long FirstRevision = 1;

    /// <summary>
    /// Mock-ups of the same design form one artwork group; a video is made from one group.
    /// </summary>
    public static string ArtworkGroupKey(Guid designImageId) => designImageId.ToString();
}

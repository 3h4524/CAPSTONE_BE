using APCS.Domain.Entities;

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

    /// <summary>
    /// The stored composites that a newer stored composite of the same design, template and garment color replaces.
    /// </summary>
    /// <remarks>
    /// Editing a template's photo or print area and making the mock-ups again adds a new row for each one: the older row
    /// may already be imported and approved, and stays so that a video made from it keeps its source. It is no longer
    /// offered for review or picked for a new video, and a scene that named it moves to the newer one by template and
    /// color. Only an imported row replaces another, so a design keeps its images until the new ones are stored.
    /// </remarks>
    public static IReadOnlySet<Guid> Superseded(IEnumerable<MockupImage> rows) =>
        rows.Where(row => row.SourceType == Generated && row.ContentHash != null && row.DeletedAt == null
                && row.DesignImageId != null && row.MockupTemplateId != null)
            .GroupBy(row => (row.DesignImageId, row.MockupTemplateId, Color: (row.VariantKey ?? row.GarmentColor)?.ToUpperInvariant()))
            .SelectMany(slot => slot.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Skip(1))
            .Select(row => row.Id)
            .ToHashSet();

    /// <summary>The rows without the ones <see cref="Superseded"/> names.</summary>
    public static IReadOnlyList<MockupImage> Current(IReadOnlyCollection<MockupImage> rows)
    {
        var superseded = Superseded(rows);
        return superseded.Count == 0 ? rows.ToArray() : rows.Where(row => !superseded.Contains(row.Id)).ToArray();
    }
}

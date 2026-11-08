using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// The designs of a product that may leave APCS: approved ones, and the ones an approved mock-up was made from
/// (approving the mock-up approved what it shows). A rejected design never qualifies.
/// </summary>
internal static class ProductDesigns
{
    private const int Limit = 20;

    public static async Task<IReadOnlyList<DesignImage>> ApprovedAsync(
        IRepository<DesignImage> designs, IRepository<MockupImage> mockups, Guid productId, CancellationToken cancellationToken)
    {
        var shownByApprovedMockup = await mockups.Query()
            .Where(mockup => mockup.ProductId == productId && mockup.DeletedAt == null && mockup.ApprovalStatus == "approved"
                && mockup.ApprovedRevision == mockup.MetadataRevision && mockup.DesignImageId != null)
            .Select(mockup => mockup.DesignImageId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await designs.Query()
            .Where(design => design.ProductId == productId && design.DeletedAt == null && design.ApprovalStatus != "rejected"
                && (design.ApprovalStatus == "approved" || shownByApprovedMockup.Contains(design.Id)))
            .OrderByDescending(design => design.IsFinal == true)
            .ThenBy(design => design.VariationIndex)
            .ThenBy(design => design.CreatedAt)
            .Take(Limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Designs are stored as public images, so their own URL serves both the preview and the original file.</summary>
    public static ShopDesignDto ToDto(DesignImage design) =>
        new(design.Id, design.ImageUrl, design.ImageWidthPx, design.ImageHeightPx,
            design.FileFormat, design.FileSizeMb, design.IsFinal == true);

    /// <summary>The file name a buyer or Printify sees, with the stored format as extension.</summary>
    public static string FileName(DesignImage design, int index)
    {
        var extension = design.FileFormat.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "" => "png",
            var value => value,
        };
        return $"design-{index}.{extension}";
    }
}

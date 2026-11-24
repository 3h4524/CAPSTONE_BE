using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Storage;

/// <summary>Finds where the person in an approved image wears or carries the product.</summary>
public interface IGarmentLocator
{
    /// <summary>
    /// The box around <paramref name="garment"/> in <paramref name="image"/>, as shares (0..1) of its width and height;
    /// null when none is found, or the clothes model is not installed or failed on the image.
    /// </summary>
    Task<Region?> LocateAsync(MockupImage image, WornGarment garment, CancellationToken cancellationToken);
}

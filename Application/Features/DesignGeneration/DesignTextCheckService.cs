using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.DesignGeneration;

/// <summary>
/// Reads the text of one saved design and keeps the result in its generation metadata (<see cref="DesignTextCheck"/>),
/// where the review grid and the Image Quality stats find it. Run by the background worker of <see cref="IDesignTextCheckQueue"/>.
/// </summary>
public sealed class DesignTextCheckService(
    IDesignTextReader reader,
    IRepository<DesignImage> designImages,
    IUnitOfWork unitOfWork)
{
    /// <summary>True when the result was saved; false when the text could not be read or the design is gone.</summary>
    public async Task<bool> CheckAsync(DesignTextCheckRequest request, CancellationToken cancellationToken = default)
    {
        var lines = await reader.ReadAsync(request.Image, cancellationToken);
        if (lines is null) return false;

        var image = await designImages.Query().SingleOrDefaultAsync(i => i.Id == request.DesignImageId, cancellationToken);
        if (image is null) return false;

        image.GenerationMetadata = DesignTextCheck.WithResult(image.GenerationMetadata, DesignTextCheck.Compare(request.Phrase, lines));
        await designImages.UpdateAsync(image, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

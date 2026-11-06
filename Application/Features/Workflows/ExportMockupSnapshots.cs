using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Internal references fixed at export creation; storage keys are never returned to the browser.</summary>
public sealed record ExportMockupSnapshot(Guid Id, Guid ProductId, long Revision, string ContentHash, string StorageKey, string? StorageVersion);

public sealed class ExportMockupSnapshots(IRepository<MockupImage> mockups)
{
    /// <summary>Capture every approved, current, non-deleted mockup belonging to the selected product.</summary>
    public async Task<IReadOnlyList<ExportMockupSnapshot>> CaptureAsync(Guid productId, CancellationToken cancellationToken)
    {
        var rows = await mockups.FindAsync(item => item.ProductId == productId && item.DeletedAt == null &&
            item.ApprovalStatus == "approved" && item.ApprovedRevision == item.MetadataRevision, cancellationToken);
        return rows.Where(item => !string.IsNullOrWhiteSpace(item.StorageKey) && !string.IsNullOrWhiteSpace(item.ContentHash))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => new ExportMockupSnapshot(item.Id, item.ProductId, item.MetadataRevision, item.ContentHash!, item.StorageKey, item.StorageVersion)).ToArray();
    }

    /// <summary>Reject a changed, deleted, moved or no-longer-approved file rather than silently omitting it.</summary>
    public async Task<Result> ValidateAsync(Guid productId, IReadOnlyList<ExportMockupSnapshot> selected, CancellationToken cancellationToken)
    {
        foreach (var expected in selected)
        {
            var current = await mockups.GetByIdAsync(expected.Id, cancellationToken);
            if (expected.ProductId != productId || current is null || current.ProductId != productId || current.DeletedAt is not null ||
                current.ApprovalStatus != "approved" || current.MetadataRevision != expected.Revision || current.ApprovedRevision != expected.Revision ||
                current.StorageKey != expected.StorageKey || current.StorageVersion != expected.StorageVersion || current.ContentHash != expected.ContentHash)
                return Result.Failure(Error.Conflict("Export.MockupChanged", "A selected mockup changed or is no longer approved."));
        }
        return Result.Success();
    }
}

using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IMockupAssetService
{
    Task<Result<IReadOnlyList<MockupResponse>>> ListAsync(Guid productId, CancellationToken ct);
    Task<Result<MockupResponse>> UploadAsync(Guid productId, Stream content, string name, long length, CancellationToken ct);
    Task<Result<MockupResponse>> UpdateAsync(Guid id, MockupMetadataRequest request, CancellationToken ct);
    Task<Result<MockupResponse>> ReviewAsync(Guid id, MockupReviewRequest request, CancellationToken ct);
}

public sealed class MockupAssetService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IWorkflowStateRepository state, IUnitOfWork uow, IMediaStorage storage, MockupMetadataValidator validator,
    VideoStoryboardPlanner planner, TimeProvider time) : IMockupAssetService
{
    public async Task<Result<IReadOnlyList<MockupResponse>>> ListAsync(Guid productId, CancellationToken ct)
    {
        if (!await OwnsAsync(productId, ct)) return Result.Failure<IReadOnlyList<MockupResponse>>(Missing());
        return Result.Success<IReadOnlyList<MockupResponse>>((await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null, ct)).OrderByDescending(x => x.CreatedAt).Select(Map).ToArray());
    }
    public async Task<Result<MockupResponse>> UploadAsync(Guid productId, Stream content, string name, long length, CancellationToken ct)
    {
        if (!await OwnsAsync(productId, ct)) return Result.Failure<MockupResponse>(Missing());
        if (length is <= 0 or > 20 * 1024 * 1024) return Result.Failure<MockupResponse>(Error.Validation("Images must be at most 20 MB."));
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > 20 * 1024 * 1024) return Result.Failure<MockupResponse>(Error.Validation("Image exceeds 20 MB."));
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (!IsStaticImage(buffer.ToArray())) return Result.Failure<MockupResponse>(Error.Validation("Use a static JPEG, PNG or WebP image."));
        buffer.Position = 0;
        var id = Guid.NewGuid();
        var saved = await storage.UploadMockupAsync(buffer, Path.GetFileName(name), $"mockups/{user.UserId}/{productId}/{id}", ct);
        var entity = new MockupImage
        {
            Id = id, ProductId = productId, SourceType = "uploaded", StorageProvider = "cloudinary", StorageKey = saved.StorageKey,
            StorageVersion = saved.Version, ContentHash = saved.Hash, MockupWidthPx = saved.Width, MockupHeightPx = saved.Height,
            Role = "Hero", ArtworkGroupKey = productId.ToString(), MetadataRevision = 1, ApprovalStatus = "pending",
            Regions = WorkflowJson.Write(new MockupRegions()), IsFinal = false, CreatedAt = time.GetUtcNow().UtcDateTime
        };
        await assets.AddAsync(entity, cancellationToken: ct); await uow.SaveChangesAsync(ct);
        return Result.Success(Map(entity));
    }
    public async Task<Result<MockupResponse>> UpdateAsync(Guid id, MockupMetadataRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Failure<MockupResponse>(Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage))));
        await using var tx = await uow.BeginTransactionAsync(ct);
        var entity = await state.LockMockupAsync(id, ct);
        if (entity is null || entity.DeletedAt != null || !await OwnsAsync(entity.ProductId, ct)) return Result.Failure<MockupResponse>(Missing());
        if (entity.MetadataRevision != request.ExpectedRevision) return Result.Failure<MockupResponse>(Error.Conflict("StaleRevision", "Mockup changed. Reload before editing."));
        entity.Role = request.Role; entity.ArtworkGroupKey = request.ArtworkGroupKey.Trim(); entity.VariantKey = request.VariantKey;
        entity.Regions = WorkflowJson.Write(request.Regions); entity.MetadataRevision++; entity.ApprovalStatus = "pending";
        entity.ApprovedRevision = null; entity.ApprovedAt = null; entity.ApprovedBy = null;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success(Map(entity));
    }
    public async Task<Result<MockupResponse>> ReviewAsync(Guid id, MockupReviewRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var entity = await state.LockMockupAsync(id, ct);
        if (entity is null || entity.DeletedAt != null || !await OwnsAsync(entity.ProductId, ct)) return Result.Failure<MockupResponse>(Missing());
        if (entity.MetadataRevision != request.ExpectedRevision) return Result.Failure<MockupResponse>(Error.Conflict("StaleRevision", "Mockup changed. Reload before reviewing."));
        entity.ApprovalStatus = request.Approved ? "approved" : "rejected"; entity.ApprovedRevision = request.Approved ? entity.MetadataRevision : null;
        entity.ApprovedBy = request.Approved ? user.UserId : null; entity.ApprovedAt = request.Approved ? time.GetUtcNow().UtcDateTime : null;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success(Map(entity));
    }
    private async Task<bool> OwnsAsync(Guid id, CancellationToken ct) => await products.GetByIdAsync(id, ct) is { DeletedAt: null } p && p.UserId == user.UserId;
    private MockupResponse Map(MockupImage x) => new(x.Id, x.ProductId, x.SourceType, x.Role, x.ArtworkGroupKey, x.VariantKey, x.MetadataRevision,
        x.ApprovalStatus, x.ApprovedRevision, x.MockupWidthPx, x.MockupHeightPx, WorkflowJson.Read<MockupRegions>(x.Regions),
        storage.SignRead(x.StorageKey, "image", x.StorageVersion), planner.CreateScene(x, "tshirt", new(), 0, 90).Warnings);
    private static Error Missing() => Error.NotFound("MockupNotFound", "Product or mockup not found.");
    public static bool IsStaticImage(byte[] bytes)
    {
        if (bytes.Length < 12) return false;
        if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return true;
        if (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            var offset = 8;
            while (offset + 12 <= bytes.Length)
            {
                var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("acTL"u8)) return false;
                if (size > int.MaxValue || size + 12L > bytes.Length - offset) return false;
                offset += (int)size + 12;
            }
            return offset == bytes.Length;
        }
        if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return false;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (size > int.MaxValue || size + 8L > bytes.Length - offset) return false;
            if (bytes.AsSpan(offset, 4).SequenceEqual("ANIM"u8) || bytes.AsSpan(offset, 4).SequenceEqual("ANMF"u8) ||
                bytes.AsSpan(offset, 4).SequenceEqual("VP8X"u8) && size > 0 && (bytes[offset + 8] & 2) != 0) return false;
            offset += 8 + (int)size + (int)(size % 2);
        }
        return true;
    }
}

namespace APCS.Application.Features.Batches.Dtos;

public sealed record BatchDto(Guid Id, string Name, string? Description, string? DefaultNiche, string? DefaultProductType, string Status, DateTime CreatedAt, int ProductCount);
public sealed record SaveBatchDto(string Name, string? Description, string? DefaultNiche, string? DefaultProductType);
public sealed record ProductDto(Guid Id, Guid BatchId, string Name, string ProductType, string? Niche, IReadOnlyList<string> Keywords, string? ProductDescription, string? SourceNotes, string Status, DateTime? CreatedAt);
public sealed record SaveProductDto(string Name, string ProductType, string? Niche, IReadOnlyList<string> Keywords, string? ProductDescription, string? SourceNotes);
public sealed record ImportBatchProductRowDto(int Row, SaveProductDto Product);
public sealed record ImportBatchProductsDto(IReadOnlyList<ImportBatchProductRowDto> Products);
public sealed record BatchProductImportErrorDto(int Row, string Name, string Message);
public sealed record BatchProductImportResultDto(int ImportedCount, IReadOnlyList<BatchProductImportErrorDto> Errors);
public sealed record ApproveBatchDto(Guid BatchId, Guid BatchJobId, int QueuedProductCount, string Status);

using APCS.Application.Features.Batches.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Features.Batches;

public interface IBatchService
{
    Task<Result<IReadOnlyList<BatchDto>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<BatchDto>> CreateAsync(SaveBatchDto request, CancellationToken cancellationToken = default);
    Task<Result<BatchDto>> UpdateAsync(Guid batchId, SaveBatchDto request, CancellationToken cancellationToken = default);
    Task<Result> DeleteBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<ApproveBatchDto>> ApproveAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ProductDto>>> ListProductsAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> AddProductAsync(Guid batchId, SaveProductDto request, CancellationToken cancellationToken = default);
    Task<Result<BatchProductImportResultDto>> ImportProductsAsync(Guid batchId, ImportBatchProductsDto request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> UpdateProductAsync(Guid batchId, Guid productId, SaveProductDto request, CancellationToken cancellationToken = default);
    Task<Result> DeleteProductAsync(Guid batchId, Guid productId, CancellationToken cancellationToken = default);
}

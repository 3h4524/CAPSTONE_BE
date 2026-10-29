using APCS.Api.Extensions;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/batches")]
public sealed class BatchesController(IBatchService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => (await service.ListAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Create(SaveBatchDto request, CancellationToken cancellationToken) => (await service.CreateAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPut("{batchId:guid}")]
    public async Task<IActionResult> Update(Guid batchId, SaveBatchDto request, CancellationToken cancellationToken) => (await service.UpdateAsync(batchId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{batchId:guid}")]
    public async Task<IActionResult> Delete(Guid batchId, CancellationToken cancellationToken) => (await service.DeleteBatchAsync(batchId, cancellationToken)).ToActionResult(this);

    [HttpPost("{batchId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid batchId, CancellationToken cancellationToken) => (await service.ApproveAsync(batchId, cancellationToken)).ToActionResult(this);

    [HttpGet("{batchId:guid}/products")]
    public async Task<IActionResult> ListProducts(Guid batchId, CancellationToken cancellationToken) => (await service.ListProductsAsync(batchId, cancellationToken)).ToActionResult(this);

    [HttpPost("{batchId:guid}/products")]
    public async Task<IActionResult> AddProduct(Guid batchId, SaveProductDto request, CancellationToken cancellationToken) => (await service.AddProductAsync(batchId, request, cancellationToken)).ToActionResult(this);

    [HttpPost("{batchId:guid}/products/import")]
    public async Task<IActionResult> ImportProducts(Guid batchId, ImportBatchProductsDto request, CancellationToken cancellationToken) => (await service.ImportProductsAsync(batchId, request, cancellationToken)).ToActionResult(this);

    [HttpPut("{batchId:guid}/products/{productId:guid}")]
    public async Task<IActionResult> EditProduct(Guid batchId, Guid productId, SaveProductDto request, CancellationToken cancellationToken) => (await service.UpdateProductAsync(batchId, productId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{batchId:guid}/products/{productId:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid batchId, Guid productId, CancellationToken cancellationToken) => (await service.DeleteProductAsync(batchId, productId, cancellationToken)).ToActionResult(this);
}

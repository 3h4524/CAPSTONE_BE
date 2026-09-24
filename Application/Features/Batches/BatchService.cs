using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Batches.Dtos;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Batches;

public sealed class BatchService(
    ICurrentUser currentUser,
    IAccountService accounts,
    IRepository<Batch> batches,
    IRepository<BatchJob> batchJobs,
    IRepository<BatchJobProduct> batchJobProducts,
    IRepository<Product> products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<SaveBatchDto> batchValidator,
    IValidator<SaveProductDto> productValidator) : IBatchService
{
    private static Error MissingBatch => Error.NotFound("Batches.NotFound", "This batch was not found.");
    private static Error MissingProduct => Error.NotFound("Products.NotFound", "This product was not found in this batch.");
    private static Error PendingOnly => Error.Conflict("MSG31", "Only pending products can be edited or deleted.");

    public async Task<Result<IReadOnlyList<BatchDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<IReadOnlyList<BatchDto>>(error);
        var userId = currentUser.UserId!.Value;
        var batchList = await batches.Query().Where(x => x.UserId == userId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt).Select(x => new BatchDto(x.Id, x.Name, x.Description, x.DefaultNiche,
                x.DefaultProductType, x.Status, x.CreatedAt,
                x.Products.Count(p => p.DeletedAt == null))).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<BatchDto>>(batchList);
    }

    public async Task<Result<BatchDto>> CreateAsync(SaveBatchDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<BatchDto>(error);
        if (await ValidateAsync(batchValidator, request, cancellationToken) is { } validationError) return Result.Failure<BatchDto>(validationError);
        var userId = currentUser.UserId!.Value;
        var name = request.Name.Trim();
        if (await batches.Query().AnyAsync(x => x.UserId == userId && x.DeletedAt == null && x.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result.Failure<BatchDto>(Error.Conflict("Batches.Duplicate", "A batch with this name already exists."));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = name, Description = Clean(request.Description),
            DefaultNiche = Clean(request.DefaultNiche), DefaultProductType = request.DefaultProductType,
            InputMethod = "manual", Status = "draft", CreatedAt = now };
        await batches.AddAsync(batch, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new BatchDto(batch.Id, batch.Name, batch.Description, batch.DefaultNiche, batch.DefaultProductType, batch.Status, now, 0));
    }

    public async Task<Result<ApproveBatchDto>> ApproveAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<ApproveBatchDto>(error);
        var userId = currentUser.UserId!.Value;
        var batch = await batches.Query().SingleOrDefaultAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (batch is null) return Result.Failure<ApproveBatchDto>(MissingBatch);

        var pendingProducts = await products.Query()
            .Where(x => x.BatchId == batchId && x.UserId == userId && x.DeletedAt == null && x.ProcessingStatus == "pending")
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        if (pendingProducts.Count == 0)
            return Result.Failure<ApproveBatchDto>(Error.Conflict("Batches.NoPendingProducts", "This batch has no pending products to queue."));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var job = new BatchJob
        {
            // Draft, not Queued: the Seller still has to configure mock-ups/prompt overrides
            // (MockupTemplateService/BatchProductPromptService both require BatchJob.Status ==
            // "draft" before allowing changes) before DesignGenerationService.StartAsync moves
            // this to Queued and actually calls the AI provider.
            Id = Guid.NewGuid(), UserId = userId, BatchId = batchId,
            Name = $"{batch.Name} - design generation", Description = batch.Description,
            SourceFileType = "manual", Status = BatchJobStatuses.Draft, JobType = "design_generation",
            Config = "{}", Priority = "normal", TotalProducts = pendingProducts.Count,
            ProcessedProducts = 0, FailedProducts = 0, SkippedProducts = 0,
            ProgressPercentage = 0, EstimatedCostUsd = 0, ActualCostUsd = 0,
            CreatedAt = now, UpdatedAt = now
        };
        await batchJobs.AddAsync(job, cancellationToken: cancellationToken);

        for (var index = 0; index < pendingProducts.Count; index++)
        {
            var product = pendingProducts[index];
            await batchJobProducts.AddAsync(new BatchJobProduct
            {
                Id = Guid.NewGuid(), BatchJobId = job.Id, BatchId = batchId,
                ProductId = product.Id, SequenceOrder = index + 1,
                Status = BatchJobProductStatuses.Pending, CurrentStep = "design_generation", RetryCount = 0,
                CreatedAt = now, UpdatedAt = now
            }, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new ApproveBatchDto(batchId, job.Id, pendingProducts.Count, job.Status));
    }

    public async Task<Result<BatchDto>> UpdateAsync(Guid batchId, SaveBatchDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<BatchDto>(error);
        if (await ValidateAsync(batchValidator, request, cancellationToken) is { } validationError) return Result.Failure<BatchDto>(validationError);
        var userId = currentUser.UserId!.Value;
        var batch = await batches.Query().SingleOrDefaultAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (batch is null) return Result.Failure<BatchDto>(MissingBatch);

        var name = request.Name.Trim();
        if (await batches.Query().AnyAsync(x => x.Id != batchId && x.UserId == userId && x.DeletedAt == null && x.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result.Failure<BatchDto>(Error.Conflict("Batches.Duplicate", "A batch with this name already exists."));

        batch.Name = name;
        batch.Description = Clean(request.Description);
        batch.DefaultNiche = Clean(request.DefaultNiche);
        batch.DefaultProductType = request.DefaultProductType;
        batch.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var productCount = await products.Query().CountAsync(x => x.BatchId == batchId && x.DeletedAt == null, cancellationToken);
        return Result.Success(new BatchDto(batch.Id, batch.Name, batch.Description, batch.DefaultNiche,
            batch.DefaultProductType, batch.Status, batch.CreatedAt, productCount));
    }

    public async Task<Result> DeleteBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure(error);
        var userId = currentUser.UserId!.Value;
        var batch = await batches.Query().SingleOrDefaultAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (batch is null) return Result.Failure(MissingBatch);

        var hasActiveJob = await batchJobs.Query().AnyAsync(x => x.BatchId == batchId && x.UserId == userId && x.DeletedAt == null
            && new[] { "queued", "pending", "running", "processing" }.Contains(x.Status.ToLower()), cancellationToken);
        if (hasActiveJob)
            return Result.Failure(Error.Conflict("Batches.ActiveJob", "A batch with an active job cannot be deleted."));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        batch.DeletedAt = now;
        batch.UpdatedAt = now;
        await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> ListProductsAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<IReadOnlyList<ProductDto>>(error);
        var userId = currentUser.UserId!.Value;
        if (!await batches.Query().AnyAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken))
            return Result.Failure<IReadOnlyList<ProductDto>>(MissingBatch);
        var rows = await products.Query().Where(x => x.BatchId == batchId && x.UserId == userId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.BatchId, x.Name, x.ProductType, x.NicheCategory, x.InputDescription, x.MainKeywords, x.Notes, x.ProcessingStatus, x.CreatedAt }).ToListAsync(cancellationToken);
        var list = rows.Select(x => new ProductDto(x.Id, x.BatchId, x.Name, x.ProductType, x.NicheCategory,
            ParseKeywords(x.MainKeywords), GetProductDescription(x.InputDescription, x.Name, x.ProductType, x.NicheCategory, x.MainKeywords),
            x.Notes, x.ProcessingStatus, x.CreatedAt)).ToArray();
        return Result.Success<IReadOnlyList<ProductDto>>(list);
    }

    public async Task<Result<ProductDto>> AddProductAsync(Guid batchId, SaveProductDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<ProductDto>(error);
        if (await ValidateAsync(productValidator, request, cancellationToken) is { } validationError) return Result.Failure<ProductDto>(validationError);
        var userId = currentUser.UserId!.Value;
        var batch = await batches.Query().SingleOrDefaultAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (batch is null) return Result.Failure<ProductDto>(MissingBatch);
        var name = request.Name.Trim();
        if (await products.Query().AnyAsync(x => x.BatchId == batchId && x.DeletedAt == null && x.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result.Failure<ProductDto>(Error.Conflict("Products.Duplicate", "A product with this name already exists in this batch."));
        var keywords = NormalizeKeywords(request.Keywords);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var product = new Product { Id = Guid.NewGuid(), UserId = userId, BatchId = batchId, Name = name,
            ProductType = request.ProductType, NicheCategory = Clean(request.Niche), MainKeywords = string.Join(", ", keywords),
            Notes = Clean(request.SourceNotes), InputDescription = BuildInputDescription(name, request.ProductType, Clean(request.Niche), keywords, request.ProductDescription),
            ProcessingStatus = "pending", CreatedAt = now };
        await products.AddAsync(product, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(product));
    }

    public async Task<Result<BatchProductImportResultDto>> ImportProductsAsync(Guid batchId, ImportBatchProductsDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<BatchProductImportResultDto>(error);
        if (request.Products is not { Count: > 0 and <= 500 })
            return Result.Failure<BatchProductImportResultDto>(Error.Validation("Import between 1 and 500 products at a time."));

        var userId = currentUser.UserId!.Value;
        var batch = await batches.Query().SingleOrDefaultAsync(x => x.Id == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (batch is null) return Result.Failure<BatchProductImportResultDto>(MissingBatch);

        var existingNames = (await products.Query().Where(x => x.BatchId == batchId && x.DeletedAt == null)
            .Select(x => x.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var errors = new List<BatchProductImportErrorDto>();
        var inserted = new List<Product>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        for (var index = 0; index < request.Products.Count; index++)
        {
            var importRow = request.Products[index];
            if (importRow is null || importRow.Product is null)
            {
                errors.Add(new BatchProductImportErrorDto(index + 2, string.Empty, "Product data is required."));
                continue;
            }

            var item = importRow.Product;
            var validation = await productValidator.ValidateAsync(item, cancellationToken);
            var row = importRow.Row;
            if (!validation.IsValid)
            {
                errors.Add(new BatchProductImportErrorDto(row, item.Name ?? string.Empty, validation.Errors[0].ErrorMessage));
                continue;
            }

            var name = item.Name.Trim();
            if (!seenNames.Add(name))
            {
                errors.Add(new BatchProductImportErrorDto(row, name, "A product with this name already exists in this batch or import file."));
                continue;
            }

            var keywords = NormalizeKeywords(item.Keywords);
            var niche = Clean(item.Niche);
            inserted.Add(new Product { Id = Guid.NewGuid(), UserId = userId, BatchId = batchId, Name = name,
                ProductType = item.ProductType, NicheCategory = niche, MainKeywords = string.Join(", ", keywords),
                Notes = Clean(item.SourceNotes), InputDescription = BuildInputDescription(name, item.ProductType, niche, keywords, item.ProductDescription),
                ProcessingStatus = "pending", CreatedAt = now });
        }

        foreach (var product in inserted) await products.AddAsync(product, cancellationToken: cancellationToken);
        if (inserted.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new BatchProductImportResultDto(inserted.Count, errors));
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(Guid batchId, Guid productId, SaveProductDto request, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure<ProductDto>(error);
        if (await ValidateAsync(productValidator, request, cancellationToken) is { } validationError) return Result.Failure<ProductDto>(validationError);
        var userId = currentUser.UserId!.Value;
        var product = (await products.FindAsync(x => x.Id == productId && x.BatchId == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken)).SingleOrDefault();
        if (product is null) return Result.Failure<ProductDto>(MissingProduct);
        if (product.ProcessingStatus != "pending") return Result.Failure<ProductDto>(PendingOnly);
        var name = request.Name.Trim();
        if (await products.Query().AnyAsync(x => x.Id != productId && x.BatchId == batchId && x.DeletedAt == null && x.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result.Failure<ProductDto>(Error.Conflict("Products.Duplicate", "A product with this name already exists in this batch."));
        var keywords = NormalizeKeywords(request.Keywords);
        product.Name = name; product.ProductType = request.ProductType; product.NicheCategory = Clean(request.Niche);
        product.MainKeywords = string.Join(", ", keywords); product.Notes = Clean(request.SourceNotes);
        product.InputDescription = BuildInputDescription(name, request.ProductType, Clean(request.Niche), keywords, request.ProductDescription);
        product.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await products.UpdateAsync(product, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(product));
    }

    public async Task<Result> DeleteProductAsync(Guid batchId, Guid productId, CancellationToken cancellationToken = default)
    {
        if (await AuthorizeAsync(cancellationToken) is { } error) return Result.Failure(error);
        var userId = currentUser.UserId!.Value;
        var product = (await products.FindAsync(x => x.Id == productId && x.BatchId == batchId && x.UserId == userId && x.DeletedAt == null, cancellationToken)).SingleOrDefault();
        if (product is null) return Result.Failure(MissingProduct);
        if (product.ProcessingStatus != "pending") return Result.Failure(PendingOnly);
        product.DeletedAt = timeProvider.GetUtcNow().UtcDateTime;
        product.UpdatedAt = product.DeletedAt;
        await products.UpdateAsync(product, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> AuthorizeAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Error.Unauthorized("Batches.Unauthenticated", "Please sign in to manage batches.");
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        if (account is null || !account.IsActive || !(await accounts.GetRolesAsync(userId, cancellationToken)).Contains(AuthConstants.UserRole, StringComparer.Ordinal))
            return Error.Forbidden("Batches.Forbidden", "Only active Seller accounts can manage batches.");
        return null;
    }

    private static async Task<Error?> ValidateAsync<T>(IValidator<T> validator, T value, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(value, ct);
        return result.IsValid ? null : Error.Validation(result.Errors[0].ErrorMessage);
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string[] NormalizeKeywords(IEnumerable<string> values) => values.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Take(13).ToArray();
    private static IReadOnlyList<string> ParseKeywords(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static ProductDto Map(Product p) => new(p.Id, p.BatchId, p.Name, p.ProductType, p.NicheCategory, ParseKeywords(p.MainKeywords),
        GetProductDescription(p.InputDescription, p.Name, p.ProductType, p.NicheCategory, p.MainKeywords), p.Notes, p.ProcessingStatus, p.CreatedAt);

    private static string BuildInputDescription(string name, string productType, string? niche, IEnumerable<string> keywords, string? productDescription)
    {
        var description = Clean(productDescription);
        return description ?? $"{name}; type: {productType}; niche: {niche}; keywords: {string.Join(", ", keywords)}";
    }

    private static string? GetProductDescription(string inputDescription, string name, string productType, string? niche, string? mainKeywords)
    {
        var generatedDescription = BuildInputDescription(name, productType, niche, ParseKeywords(mainKeywords), null);
        return string.Equals(inputDescription, generatedDescription, StringComparison.Ordinal) ? null : inputDescription;
    }
}

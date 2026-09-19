using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Lists mock-up templates and manages per-batch mock-up selection.</summary>
public interface IMockupTemplateService
{
    /// <summary>Lists active templates, optionally filtered by product type.</summary>
    Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, CancellationToken cancellationToken = default);

    /// <summary>Reads the mock-up selection stored on a batch.</summary>
    Task<Result<BatchMockupSelectionResponseDto>> GetSelectionAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>Stores the mock-up selection on a draft batch.</summary>
    Task<Result<BatchMockupSelectionResponseDto>> ApplyAsync(Guid batchJobId, ApplyMockupTemplatesRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Creates a seller-owned mock-up template.</summary>
    Task<Result<MockupTemplateResponseDto>> CreateAsync(CreateMockupTemplateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Updates a seller-owned mock-up template.</summary>
    Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, UpdateMockupTemplateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a seller-owned mock-up template and its images.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

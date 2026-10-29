using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using APCS.Application.Features.BatchProductPrompts.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.BatchProductPrompts;

/// <summary>Reads and stores per-row design prompt overrides.</summary>
public interface IBatchProductPromptService
{
    /// <summary>Reads the default synthesized prompt of one batch row.</summary>
    Task<Result<BatchProductPromptResponseDto>> GetDefaultAsync(Guid rowId, CancellationToken cancellationToken = default);

    /// <summary>Reads the effective prompt of one batch row, preferring stored overrides.</summary>
    Task<Result<BatchProductPromptResponseDto>> GetEffectiveAsync(Guid rowId, CancellationToken cancellationToken = default);

    /// <summary>Stores the prompt override of one pending batch row.</summary>
    Task<Result<BatchProductPromptResponseDto>> SaveAsync(Guid rowId, UpdateBatchProductPromptRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Clears the prompt override of one pending batch row back to default.</summary>
    Task<Result<BatchProductPromptResponseDto>> RestoreAsync(Guid rowId, CancellationToken cancellationToken = default);
}

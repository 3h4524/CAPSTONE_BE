using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.DesignTemplates;

/// <summary>Provides Seller design-template library use cases.</summary>
public interface IDesignTemplateService
{
    Task<Result<PagedResult<DesignTemplateSummaryResponseDto>>> ListAsync(
        ListDesignTemplatesRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<DesignTemplateDetailResponseDto>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<DesignTemplateOptionsResponseDto>> GetOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<DesignTemplateDetailResponseDto>> CreateAsync(
        CreateDesignTemplateRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<DesignTemplateDetailResponseDto>> UpdateAsync(
        Guid id,
        UpdateDesignTemplateRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<DesignTemplateDetailResponseDto>> CloneAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Result<ResolvedDesignTemplatePromptResponseDto>> ResolveForPromptAsync(
        Guid sellerId,
        ResolveDesignTemplatePromptRequestDto request,
        CancellationToken cancellationToken = default);
}

using APCS.Application.Features.Admin.Moderation.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.Moderation;

/// <summary>The admin Content Safety page: what the guardrail decided, its blocklist and its settings.</summary>
public interface IAdminModerationService
{
    Task<Result<ModerationOverviewDto>> GetOverviewAsync(int days, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ModerationOffenderDto>>> GetTopOffendersAsync(int days, int limit,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ModerationCheckListItemDto>>> ListChecksAsync(ListModerationChecksRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<ModerationCheckDetailDto>> GetCheckAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ModerationTermDto>>> ListTermsAsync(ListModerationTermsRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<ModerationTermDto>> CreateTermAsync(SaveModerationTermRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<ModerationTermDto>> UpdateTermAsync(Guid id, SaveModerationTermRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteTermAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ModerationAllowlistDto>>> ListAllowlistAsync(CancellationToken cancellationToken = default);

    Task<Result<ModerationAllowlistDto>> CreateAllowlistAsync(SaveModerationAllowlistRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<ModerationAllowlistDto>> UpdateAllowlistAsync(Guid id, SaveModerationAllowlistRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAllowlistAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ModerationSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<Result<ModerationSettingsDto>> UpdateSettingsAsync(UpdateModerationSettingsRequestDto request,
        CancellationToken cancellationToken = default);
}

using APCS.Application.Features.Onboarding.Dtos.Request;
using APCS.Application.Features.Onboarding.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Onboarding;

/// <summary>
/// The authenticated user's onboarding state: dashboard checklist progress and seen page guides.
/// </summary>
public interface IOnboardingProgressService
{
    Task<Result<OnboardingProgressResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<OnboardingProgressResponse>> UpdateAsync(
        UpdateOnboardingProgressRequest request,
        CancellationToken cancellationToken = default);
}

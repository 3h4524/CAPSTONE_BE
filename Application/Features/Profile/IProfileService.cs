using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.Features.Profile.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Profile;

/// <summary>
/// Provides the Profile feature's use cases.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Gets the authenticated user's profile.
    /// </summary>
    Task<Result<ProfileResponseDto>> GetProfileAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Partially updates the authenticated user's profile.
    /// </summary>
    Task<Result<ProfileResponseDto>> UpdateProfileAsync(
        UpdateProfileRequestDto request,
        CancellationToken cancellationToken = default);
}

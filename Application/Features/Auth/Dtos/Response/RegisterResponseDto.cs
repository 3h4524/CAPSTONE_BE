namespace APCS.Application.Features.Auth.Dtos.Response;

/// <summary>
/// Represents a successful register response.
/// </summary>
/// <param name="UserId">The identifier of the created account.</param>
/// <param name="Email">The address the verification link was sent to.</param>
/// <param name="RequiresEmailVerification">
/// Always true for a password registration: no session is issued because the account stays
/// pending verification until its emailed link is redeemed.
/// </param>
public sealed record RegisterResponseDto(
    Guid UserId,
    string Email,
    bool RequiresEmailVerification);

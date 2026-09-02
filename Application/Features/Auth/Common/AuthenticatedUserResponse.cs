namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Represents authenticated user data returned by auth endpoints.
/// </summary>
public sealed record AuthenticatedUserResponse(
    int Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);

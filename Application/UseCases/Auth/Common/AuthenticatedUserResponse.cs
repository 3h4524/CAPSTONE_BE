namespace APCS.Application.UseCases.Auth.Common;

/// <summary>
/// Represents authenticated user data returned by auth endpoints.
/// </summary>
public sealed record AuthenticatedUserResponse(
    Guid Id,
    string Email,
    string? FullName,
    IReadOnlyCollection<string> Roles);

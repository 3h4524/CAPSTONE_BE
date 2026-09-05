namespace APCS.Application.Abstractions.Authentication.Models;

/// <summary>
/// Represents user data needed by application use cases.
/// </summary>
public sealed record IdentityUserInfo(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool IsLockedOut);

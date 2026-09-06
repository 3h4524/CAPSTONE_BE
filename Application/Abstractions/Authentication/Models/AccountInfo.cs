namespace APCS.Application.Abstractions.Authentication.Models;

/// <summary>
/// Represents account data needed by authentication use cases.
/// </summary>
public sealed record AccountInfo(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive);

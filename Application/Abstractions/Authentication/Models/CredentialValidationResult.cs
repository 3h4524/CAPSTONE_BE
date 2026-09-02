namespace APCS.Application.Abstractions.Authentication.Models;

/// <summary>
/// Represents the outcome of a credential validation attempt.
/// </summary>
/// <param name="Succeeded">Indicates whether the credentials are valid.</param>
/// <param name="IsLockedOut">Indicates whether the account is now locked out.</param>
/// <param name="IsNotAllowed">Indicates whether sign-in is not allowed.</param>
public sealed record CredentialValidationResult(
    bool Succeeded,
    bool IsLockedOut,
    bool IsNotAllowed);

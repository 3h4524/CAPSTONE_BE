namespace APCS.Application.Abstractions.Authentication.Dtos;

/// <summary>
/// Represents account data needed by authentication use cases.
/// </summary>
/// <param name="Id">The user identifier.</param>
/// <param name="Email">The registered email address.</param>
/// <param name="FullName">The account holder's full name.</param>
/// <param name="IsActive">Indicates whether the account status allows authentication.</param>
/// <param name="IsEmailVerified">
/// Indicates whether the email address has been verified. A freshly registered account is
/// unverified and stays inactive until its verification link is redeemed, so callers check this
/// before the generic inactive check to explain why the account cannot be used yet.
/// </param>
public sealed record AccountInfoDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool IsEmailVerified);

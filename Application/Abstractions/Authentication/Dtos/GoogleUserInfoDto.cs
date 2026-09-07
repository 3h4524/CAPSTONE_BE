namespace APCS.Application.Abstractions.Authentication.Dtos;

/// <summary>
/// Represents the identity Google vouched for in a verified ID token.
/// </summary>
/// <param name="GoogleId">Google's stable subject identifier for the account.</param>
/// <param name="Email">The Google account's email address.</param>
/// <param name="EmailVerified">Whether Google has verified this email address.</param>
/// <param name="FullName">The account holder's display name, when Google provided one.</param>
/// <param name="AvatarUrl">The account holder's profile picture, when Google provided one.</param>
public sealed record GoogleUserInfoDto(
    string GoogleId,
    string Email,
    bool EmailVerified,
    string? FullName,
    string? AvatarUrl);

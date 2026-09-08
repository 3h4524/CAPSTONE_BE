namespace APCS.Application.Abstractions.Authentication.Dtos;

/// <summary>
/// Represents the result of staging a new account and its default role.
/// </summary>
public sealed record AccountCreationResultDto(
    bool Succeeded,
    IReadOnlyCollection<string> Errors,
    AccountInfoDto? User,
    IReadOnlyCollection<string> Roles)
{
    public static AccountCreationResultDto Success(AccountInfoDto user, IReadOnlyCollection<string> roles) =>
        new(true, Array.Empty<string>(), user, roles);

    public static AccountCreationResultDto Failure(params string[] errors) =>
        new(false, errors, null, Array.Empty<string>());
}

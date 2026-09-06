namespace APCS.Application.Abstractions.Authentication.Models;

/// <summary>
/// Represents the result of staging a new account and its default role.
/// </summary>
public sealed record AccountCreationResult(
    bool Succeeded,
    IReadOnlyCollection<string> Errors,
    AccountInfo? User,
    IReadOnlyCollection<string> Roles)
{
    public static AccountCreationResult Success(AccountInfo user, IReadOnlyCollection<string> roles) =>
        new(true, Array.Empty<string>(), user, roles);

    public static AccountCreationResult Failure(params string[] errors) =>
        new(false, errors, null, Array.Empty<string>());
}

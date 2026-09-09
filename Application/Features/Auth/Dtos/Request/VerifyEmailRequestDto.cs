namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Redeems an email verification token and activates the account.
/// </summary>
/// <param name="Token">The raw token taken from the emailed verification link.</param>
/// <remarks>Use case mapping: UC11.</remarks>
public sealed record VerifyEmailRequestDto(string Token);

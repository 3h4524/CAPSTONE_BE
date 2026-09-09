using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Registers a new account.
/// </summary>
/// <remarks>Use case mapping: UC01.</remarks>
public sealed record RegisterRequestDto(
    string Email,
    string Password,
    string? FullName = null,
    RequestContext? Context = null);

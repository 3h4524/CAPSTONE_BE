using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Logs a user in with email and password.
/// </summary>
/// <remarks>Use case mapping: UC02.</remarks>
public sealed record LoginRequestDto(
    string Email,
    string Password,
    RequestContext? Context = null);

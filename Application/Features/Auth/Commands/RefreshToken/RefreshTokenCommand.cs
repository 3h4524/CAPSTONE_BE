using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Refreshes an access token using a refresh token cookie.
/// </summary>
/// <remarks>Use case mapping: UC02b.</remarks>
public sealed record RefreshTokenCommand(
    string? RefreshToken,
    RequestContext? Context = null) : IRequest<Result<RefreshTokenResponse>>;

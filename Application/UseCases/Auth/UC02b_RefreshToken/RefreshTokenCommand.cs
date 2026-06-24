using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC02b_RefreshToken;

/// <summary>
/// Refreshes an access token using a refresh token cookie.
/// </summary>
public sealed record RefreshTokenCommand(
    string? RefreshToken,
    string? IpAddress) : IRequest<Result<RefreshTokenResponse>>;

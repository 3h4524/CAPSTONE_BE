using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC02c_Logout;

/// <summary>
/// Logs out the current refresh token session.
/// </summary>
public sealed record LogoutCommand(
    string? RefreshToken,
    string? IpAddress) : IRequest<Result>;

using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Logs out the current refresh token session.
/// </summary>
/// <remarks>Use case mapping: UC02c.</remarks>
public sealed record LogoutCommand(
    string? RefreshToken,
    RequestContext? Context = null) : IRequest<Result>;

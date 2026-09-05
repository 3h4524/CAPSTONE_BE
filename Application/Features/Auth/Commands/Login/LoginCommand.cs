using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Logs a user in with email and password.
/// </summary>
/// <remarks>Use case mapping: UC02.</remarks>
public sealed record LoginCommand(
    string Email,
    string Password,
    RequestContext? Context = null) : IRequest<Result<LoginResponse>>;

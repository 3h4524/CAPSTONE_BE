using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Logs a seller in with email and password.
/// </summary>
/// <remarks>Use case mapping: UC02.</remarks>
public sealed record LoginCommand(
    string Email,
    string Password) : IRequest<Result<LoginResponse>>;

using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC02_Login;

/// <summary>
/// Logs a seller in with email and password.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress) : IRequest<Result<LoginResponse>>;

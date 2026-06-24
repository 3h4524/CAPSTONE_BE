using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC01_Register;

/// <summary>
/// Registers a new seller account.
/// </summary>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string? FullName,
    string? IpAddress) : IRequest<Result<RegisterResponse>>;

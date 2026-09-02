using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Register;

/// <summary>
/// Registers a new seller account.
/// </summary>
/// <remarks>Use case mapping: UC01.</remarks>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string FullName) : IRequest<Result<RegisterResponse>>;

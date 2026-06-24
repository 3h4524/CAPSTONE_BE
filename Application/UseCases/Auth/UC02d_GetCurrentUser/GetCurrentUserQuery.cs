using APCS.Application.UseCases.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC02d_GetCurrentUser;

/// <summary>
/// Gets the current authenticated user.
/// </summary>
public sealed record GetCurrentUserQuery : IRequest<Result<AuthenticatedUserResponse>>;

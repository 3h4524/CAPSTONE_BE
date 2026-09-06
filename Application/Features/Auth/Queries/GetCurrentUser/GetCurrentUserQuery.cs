using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Queries.GetCurrentUser;

/// <summary>
/// Gets the current authenticated user.
/// </summary>
/// <remarks>Use case mapping: UC02d.</remarks>
public sealed record GetCurrentUserQuery : IRequest<Result<AuthenticatedUserResponse>>;

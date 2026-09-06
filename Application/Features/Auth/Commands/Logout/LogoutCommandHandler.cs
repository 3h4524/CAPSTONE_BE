using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Handles refresh token revocation during logout.
/// </summary>
public sealed class LogoutCommandHandler(
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<LogoutCommand, Result>
{
    /// <inheritdoc />
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Success();
        }

        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var existingToken = await authTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existingToken is not null && !existingToken.IsRevoked)
        {
            existingToken.Revoke(timeProvider.GetUtcNow());

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent logout or refresh revoked the same token first. The session is
                // gone either way, which is all logout promises, so this stays a success.
            }
        }

        return Result.Success();
    }
}

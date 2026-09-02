using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Handles refresh token revocation during logout.
/// </summary>
public sealed class LogoutCommandHandler(
    IUnitOfWork unitOfWork,
    IRefreshTokenRepository refreshTokenRepository,
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
        var existingToken = await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existingToken is not null && !existingToken.IsRevoked)
        {
            existingToken.Revoke(timeProvider.GetUtcNow(), "Logout");
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

using APCS.Application.Common.Interfaces;
using APCS.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.UseCases.Auth.UC02c_Logout;

/// <summary>
/// Handles refresh token revocation during logout.
/// </summary>
public sealed class LogoutCommandHandler(
    IAppDbContext dbContext,
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
        var existingToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existingToken is not null && !existingToken.IsRevoked)
        {
            existingToken.Revoke(timeProvider.GetUtcNow(), request.IpAddress, "Logout");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

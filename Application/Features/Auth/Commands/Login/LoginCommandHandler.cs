using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Handles seller login.
/// </summary>
public sealed class LoginCommandHandler(
    IIdentityService identityService,
    IUnitOfWork unitOfWork,
    IRefreshTokenRepository refreshTokenRepository,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    /// <inheritdoc />
    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await identityService.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials());
        }

        if (!user.IsActive)
        {
            return Result.Failure<LoginResponse>(AuthErrors.Inactive());
        }

        if (user.IsLockedOut)
        {
            return Result.Failure<LoginResponse>(AuthErrors.LockedOut());
        }

        var credentialResult = await identityService.ValidateCredentialsAsync(user.Id, request.Password, cancellationToken);
        if (credentialResult.IsLockedOut)
        {
            return Result.Failure<LoginResponse>(AuthErrors.LockedOut());
        }

        if (credentialResult.IsNotAllowed)
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials());
        }

        if (!credentialResult.Succeeded)
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials());
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow);

        refreshTokenRepository.Add(session.Entity);
        await identityService.TouchLastLoginAsync(user.Id, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new LoginResponse(
            session.AccessToken.AccessToken,
            session.AccessToken.ExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            new AuthenticatedUserResponse(user.Id, user.Email, user.FullName, roles));

        return Result.Success(response);
    }
}

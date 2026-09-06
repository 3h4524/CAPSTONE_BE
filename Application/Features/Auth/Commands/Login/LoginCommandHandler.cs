using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Handles user login.
/// </summary>
public sealed class LoginCommandHandler(
    IAccountService accountService,
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    /// <inheritdoc />
    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await accountService.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials());
        }

        if (!user.IsActive)
        {
            return Result.Failure<LoginResponse>(AuthErrors.Inactive());
        }

        if (!await accountService.ValidateCredentialsAsync(user.Id, request.Password, cancellationToken))
        {
            return Result.Failure<LoginResponse>(AuthErrors.InvalidCredentials());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var session = AuthSessionFactory.Create(
            jwtService,
            user,
            roles,
            utcNow,
            request.Context ?? RequestContext.None);

        authTokenRepository.Add(session.Entity);
        await accountService.TouchLastLoginAsync(user.Id, utcNow, cancellationToken);
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

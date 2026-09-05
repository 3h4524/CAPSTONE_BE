using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Commands.Register;

/// <summary>
/// Handles account registration.
/// </summary>
public sealed class RegisterCommandHandler(
    IIdentityService identityService,
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterCommand, Result<RegisterResponse>>
{
    /// <inheritdoc />
    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await identityService.EmailExistsAsync(email, cancellationToken))
        {
            return Result.Failure<RegisterResponse>(AuthErrors.EmailAlreadyExists());
        }

        // Optional for callers that never collected one; the local part of the email is a
        // reasonable display name until the user sets their own.
        var fullName = string.IsNullOrWhiteSpace(request.FullName)
            ? email.Split('@')[0]
            : request.FullName.Trim();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var createResult = await identityService.CreateUserAsync(
                email,
                request.Password,
                fullName,
                cancellationToken);

            if (!createResult.Succeeded || createResult.User is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RegisterResponse>(AuthErrors.RegistrationFailed(createResult.Errors));
            }

            var user = createResult.User;
            var roles = createResult.Roles;
            var utcNow = timeProvider.GetUtcNow();
            var session = AuthSessionFactory.Create(
                jwtService,
                user,
                roles,
                utcNow,
                request.Context ?? RequestContext.None);

            authTokenRepository.Add(session.Entity);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new RegisterResponse(
                session.AccessToken.AccessToken,
                session.AccessToken.ExpiresAtUtc,
                session.RefreshToken,
                session.RefreshTokenExpiresAtUtc,
                new AuthenticatedUserResponse(user.Id, user.Email, user.FullName, roles));

            return Result.Success(response);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

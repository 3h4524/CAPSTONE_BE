using APCS.Application.Common.Interfaces;
using APCS.Application.UseCases.Auth.Common;
using APCS.Common.Constants;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.UseCases.Auth.UC01_Register;

/// <summary>
/// Handles seller registration.
/// </summary>
public sealed class RegisterCommandHandler(
    IIdentityService identityService,
    IAppDbContext dbContext,
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
            return Result.Failure<RegisterResponse>(
                Error.Conflict(ErrorCodes.EmailAlreadyExists, "Email is already registered."));
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            var createResult = await identityService.CreateUserAsync(
                email,
                request.Password,
                request.FullName?.Trim(),
                cancellationToken);

            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                var details = new Dictionary<string, string[]>
                {
                    ["identity"] = createResult.Errors.ToArray()
                };

                return Result.Failure<RegisterResponse>(
                    Error.Validation("Could not create the account.", details));
            }

            var user = await identityService.FindByEmailAsync(email, cancellationToken);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RegisterResponse>(
                    Error.Failure(ErrorCodes.Unexpected, "The account was created but could not be loaded."));
            }

            var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
            var utcNow = timeProvider.GetUtcNow();
            var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow, request.IpAddress);

            dbContext.RefreshTokens.Add(session.Entity);
            await dbContext.SaveChangesAsync(cancellationToken);
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

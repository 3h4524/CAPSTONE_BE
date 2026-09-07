using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Models;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Auth;

/// <summary>
/// Implements the Auth feature's use cases.
/// </summary>
public sealed class AuthService(
    IAccountService accountService,
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IJwtService jwtService,
    IGoogleAuthService googleAuthService,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<LoginRequestDto> loginValidator,
    IValidator<GoogleLoginRequestDto> googleLoginValidator)
    : IAuthService
{
    /// <inheritdoc />
    public async Task<Result<LoginResponseDto>> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await loginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<LoginResponseDto>(validation.ToValidationError());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await accountService.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.InvalidCredentials());
        }

        // Checked before the generic inactive result: an account pending verification is inactive
        // for the same reason every time, and only this answer tells the caller it can resend the
        // verification link instead of contacting support.
        if (!user.IsEmailVerified)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.EmailNotVerified());
        }

        if (!user.IsActive)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.Inactive());
        }

        if (!await accountService.ValidateCredentialsAsync(user.Id, request.Password, cancellationToken))
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.InvalidCredentials());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var session = AuthSessionFactory.Create(
            jwtService,
            user,
            roles,
            utcNow,
            request.Context ?? RequestContext.None);

        await authTokenRepository.AddAsync(session.Entity, cancellationToken: cancellationToken);
        await accountService.TouchLastLoginAsync(user.Id, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new LoginResponseDto(
            session.AccessToken.AccessToken,
            session.AccessToken.ExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            new AuthenticatedUserResponse(user.Id, user.Email, user.FullName, roles));

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<LoginResponseDto>> GoogleLoginAsync(
        GoogleLoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await googleLoginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<LoginResponseDto>(validation.ToValidationError());
        }

        var googleUser = await googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        if (googleUser is null)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.GoogleTokenInvalid());
        }

        if (!googleUser.EmailVerified)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.GoogleEmailNotVerified());
        }

        var utcNow = timeProvider.GetUtcNow();
        var resolved = await ResolveGoogleAccountAsync(googleUser, utcNow, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<LoginResponseDto>(resolved.Error);
        }

        var account = resolved.Value;
        if (!account.IsActive)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.Inactive());
        }

        var roles = await accountService.GetRolesAsync(account.Id, cancellationToken);
        var session = AuthSessionFactory.Create(
            jwtService,
            account,
            roles,
            utcNow,
            request.Context ?? RequestContext.None);

        await authTokenRepository.AddAsync(session.Entity, cancellationToken: cancellationToken);
        await accountService.TouchLastLoginAsync(account.Id, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var response = new LoginResponseDto(
            session.AccessToken.AccessToken,
            session.AccessToken.ExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            new AuthenticatedUserResponse(account.Id, account.Email, account.FullName, roles));

        return Result.Success(response);
    }

    /// <summary>
    /// Finds the account a verified Google identity belongs to, linking it to a matching
    /// password account on first Google sign-in or registering a brand-new one.
    /// </summary>
    private async Task<Result<AccountInfoDto>> ResolveGoogleAccountAsync(
        GoogleUserInfoDto googleUser,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var byGoogleId = await accountService.FindByGoogleIdAsync(googleUser.GoogleId, cancellationToken);
        if (byGoogleId is not null)
        {
            return Result.Success(byGoogleId);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        var byEmail = await accountService.FindByEmailAsync(email, cancellationToken);
        if (byEmail is not null)
        {
            // The address already belongs to a password account: Google has now vouched for the
            // same email, so the identities are linked instead of creating a duplicate account.
            var linked = await accountService.LinkGoogleIdentityAsync(
                byEmail.Id,
                googleUser.GoogleId,
                googleUser.AvatarUrl,
                utcNow,
                cancellationToken);

            if (linked is null)
            {
                return Result.Failure<AccountInfoDto>(AuthErrors.UserNotFound());
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(linked);
        }

        var fullName = string.IsNullOrWhiteSpace(googleUser.FullName)
            ? email.Split('@')[0]
            : googleUser.FullName.Trim();

        var creation = await accountService.CreateGoogleUserAsync(
            email,
            fullName,
            googleUser.GoogleId,
            googleUser.AvatarUrl,
            cancellationToken);

        if (!creation.Succeeded || creation.User is null)
        {
            return Result.Failure<AccountInfoDto>(AuthErrors.RegistrationFailed(creation.Errors));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(creation.User);
    }

    /// <inheritdoc />
    public async Task<Result<RefreshTokenResponseDto>> RefreshTokenAsync(
        string? refreshToken,
        RequestContext? context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenMissing());
        }

        var utcNow = timeProvider.GetUtcNow();
        var tokenHash = jwtService.HashRefreshToken(refreshToken);
        var existingToken = await authTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenInvalid());
        }

        if (existingToken.IsRevoked)
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenReused());
        }

        if (existingToken.IsExpired(utcNow))
        {
            existingToken.Revoke(utcNow);
            if (!await TrySaveAsync(cancellationToken))
            {
                return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenReused());
            }

            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenExpired());
        }

        var user = await accountService.FindByIdAsync(existingToken.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenInvalid());
        }

        if (!user.IsActive)
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.Inactive());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow, context ?? RequestContext.None);

        existingToken.Revoke(utcNow);
        await authTokenRepository.AddAsync(session.Entity, cancellationToken: cancellationToken);

        if (!await TrySaveAsync(cancellationToken))
        {
            return Result.Failure<RefreshTokenResponseDto>(AuthErrors.RefreshTokenReused());
        }

        var response = new RefreshTokenResponseDto(
            session.AccessToken.AccessToken,
            session.AccessToken.ExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc);

        return Result.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result> LogoutAsync(
        string? refreshToken,
        RequestContext? context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        var tokenHash = jwtService.HashRefreshToken(refreshToken);
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

    /// <inheritdoc />
    public async Task<Result<AuthenticatedUserResponse>> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<AuthenticatedUserResponse>(AuthErrors.Unauthenticated());
        }

        var user = await accountService.FindByIdAsync(currentUser.UserId.Value, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AuthenticatedUserResponse>(AuthErrors.UserNotFound());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        return Result.Success(new AuthenticatedUserResponse(user.Id, user.Email, user.FullName, roles));
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}

using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Constants;
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
    IEmailService emailService,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<VerifyEmailRequestDto> verifyEmailValidator,
    IValidator<ResendVerificationEmailRequestDto> resendVerificationEmailValidator)
    : IAuthService
{
    /// <inheritdoc />
    public async Task<Result> VerifyEmailAsync(
        VerifyEmailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await verifyEmailValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        var tokenHash = jwtService.HashEmailVerificationToken(request.Token);
        var token = await authTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        // A refresh token would also resolve by hash, so the kind is checked before anything is
        // redeemed: only a verification token may activate an account.
        if (token is null || token.TokenType != AuthTokenTypes.EmailVerification)
        {
            return Result.Failure(AuthErrors.VerificationTokenInvalid());
        }

        var utcNow = timeProvider.GetUtcNow();
        if (token.IsExpired(utcNow))
        {
            return Result.Failure(AuthErrors.VerificationTokenExpired());
        }

        if (!token.IsActive(utcNow))
        {
            return Result.Failure(AuthErrors.VerificationTokenInvalid());
        }

        var account = await accountService.FindByIdAsync(token.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(AuthErrors.VerificationTokenInvalid());
        }

        // An account verified through an earlier link needs no second activation, but the token
        // presented here is still spent so it cannot be replayed.
        if (!account.IsEmailVerified
            && !await accountService.ConfirmEmailAsync(account.Id, utcNow, cancellationToken))
        {
            return Result.Failure(AuthErrors.VerificationTokenInvalid());
        }

        token.MarkUsed(utcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always reports success. An unknown address and an already verified account are both answered
    /// the same way as a genuine resend, so the endpoint cannot be used to discover which addresses
    /// are registered.
    /// </remarks>
    public async Task<Result> ResendVerificationEmailAsync(
        ResendVerificationEmailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await resendVerificationEmailValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var account = await accountService.FindByEmailAsync(email, cancellationToken);

        if (account is null || account.IsEmailVerified)
        {
            return Result.Success();
        }

        var utcNow = timeProvider.GetUtcNow();

        // The database keeps at most one redeemable token per user and kind, and expiry does not
        // release that slot. The link already sent is therefore revoked before its replacement is
        // issued, which also stops an old link from still working after a resend.
        var outstanding = await authTokenRepository.GetRedeemableAsync(
            account.Id,
            AuthTokenTypes.EmailVerification,
            cancellationToken);

        foreach (var token in outstanding)
        {
            token.Revoke(utcNow);
        }

        var verification = EmailVerificationFactory.Create(
            jwtService,
            account.Id,
            utcNow,
            request.Context ?? RequestContext.None);

        await authTokenRepository.AddAsync(verification.Entity, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emailService.SendEmailVerificationAsync(
            account.Email,
            account.FullName,
            verification.Token,
            cancellationToken);

        return Result.Success();
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

using System.Security.Cryptography;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Helpers;
using APCS.Common.Models;
using APCS.Application.Abstractions.Authentication.Dtos;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    IEmailService emailService,
    ICacheService cacheService,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<LoginRequestDto> loginValidator,
    IValidator<GoogleLoginRequestDto> googleLoginValidator,
    ILogger<AuthService> logger,
    IValidator<RegisterRequestDto> registerValidator,
    IValidator<VerifyEmailRequestDto> verifyEmailValidator,
    IValidator<ResendVerificationEmailRequestDto> resendVerificationEmailValidator,
    IValidator<ForgotPasswordRequestDto> forgotPasswordValidator,
    IValidator<ResetPasswordRequestDto> resetPasswordValidator,
    IValidator<ChangePasswordRequestDto> changePasswordValidator,
    IValidator<AdminVerifyTwoFactorRequestDto> adminVerifyTwoFactorValidator,
    IValidator<AdminResendTwoFactorRequestDto> adminResendTwoFactorValidator)
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

        if (roles.Any(role => string.Equals(role, AuthConstants.AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            var challenge = await CreateAdminTwoFactorChallengeAsync(
                user,
                request.Context ?? RequestContext.None,
                cancellationToken);

            return Result.Success(new LoginResponseDto(
                null,
                null,
                null,
                null,
                null,
                RequiresTwoFactor: true,
                TempToken: challenge.TempToken,
                TwoFactorExpiresAtUtc: challenge.ExpiresAtUtc));
        }

        return await IssueSessionAsync(user, roles, request.Context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<LoginResponseDto>> VerifyAdminTwoFactorAsync(
        AdminVerifyTwoFactorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await adminVerifyTwoFactorValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<LoginResponseDto>(validation.ToValidationError());
        }

        var cacheKey = BuildAdminTwoFactorCacheKey(request.TempToken);
        var challenge = await cacheService.GetAsync<AdminTwoFactorChallenge>(cacheKey, cancellationToken);
        if (challenge is null || !string.Equals(challenge.Status, "pending", StringComparison.Ordinal))
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        var utcNow = timeProvider.GetUtcNow();
        if (challenge.ExpiresAtUtc <= utcNow)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.AdminTwoFactorExpired());
        }

        var presentedHash = HashHelper.ComputeSha256Hash($"{request.TempToken}:{request.OtpCode}");
        if (!HashHelper.FixedTimeEquals(challenge.OtpHash, presentedHash))
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        await cacheService.RemoveAsync(cacheKey, cancellationToken);

        var account = await accountService.FindByIdAsync(challenge.UserId, cancellationToken);
        if (account is null || !account.IsActive || !account.IsEmailVerified)
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        var roles = await accountService.GetRolesAsync(account.Id, cancellationToken);
        if (!roles.Any(role => string.Equals(role, AuthConstants.AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LoginResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        return await IssueSessionAsync(
            account,
            roles,
            request.Context ?? challenge.RequestContext,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AdminTwoFactorResponseDto>> ResendAdminTwoFactorAsync(
        AdminResendTwoFactorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await adminResendTwoFactorValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<AdminTwoFactorResponseDto>(validation.ToValidationError());
        }

        var cacheKey = BuildAdminTwoFactorCacheKey(request.TempToken);
        var current = await cacheService.GetAsync<AdminTwoFactorChallenge>(cacheKey, cancellationToken);
        if (current is null || !string.Equals(current.Status, "pending", StringComparison.Ordinal))
        {
            return Result.Failure<AdminTwoFactorResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        var account = await accountService.FindByIdAsync(current.UserId, cancellationToken);
        var roles = account is null
            ? Array.Empty<string>()
            : await accountService.GetRolesAsync(account.Id, cancellationToken);
        if (account is null
            || !account.IsActive
            || !account.IsEmailVerified
            || !roles.Any(role => string.Equals(role, AuthConstants.AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<AdminTwoFactorResponseDto>(AuthErrors.AdminTwoFactorInvalid());
        }

        var utcNow = timeProvider.GetUtcNow();
        var expiresAtUtc = utcNow.AddMinutes(AuthConstants.AdminTwoFactorCodeMinutes);
        var otpCode = GenerateOtpCode();
        var replacement = current with
        {
            Email = account.Email,
            FullName = account.FullName,
            OtpHash = HashHelper.ComputeSha256Hash($"{request.TempToken}:{otpCode}"),
            ExpiresAtUtc = expiresAtUtc,
            RequestContext = request.Context ?? current.RequestContext
        };

        await cacheService.SetAsync(
            cacheKey,
            replacement,
            TimeSpan.FromMinutes(AuthConstants.AdminTwoFactorChallengeRetentionMinutes),
            cancellationToken);
        await SendAdminOtpAsync(account.Email, account.FullName, otpCode, cancellationToken);

        return Result.Success(new AdminTwoFactorResponseDto(request.TempToken, expiresAtUtc));
    }

    private async Task<Result<LoginResponseDto>> IssueSessionAsync(
        AccountInfoDto user,
        IReadOnlyCollection<string> roles,
        RequestContext? requestContext,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();
        var session = AuthSessionFactory.Create(
            jwtService,
            user,
            roles,
            utcNow,
            requestContext ?? RequestContext.None);

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

    private async Task<AdminTwoFactorResponseDto> CreateAdminTwoFactorChallengeAsync(
        AccountInfoDto account,
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var tempToken = GenerateOpaqueToken();
        var otpCode = GenerateOtpCode();
        var expiresAtUtc = timeProvider.GetUtcNow().AddMinutes(AuthConstants.AdminTwoFactorCodeMinutes);
        var challenge = new AdminTwoFactorChallenge(
            account.Id,
            account.Email,
            account.FullName,
            HashHelper.ComputeSha256Hash($"{tempToken}:{otpCode}"),
            "pending",
            expiresAtUtc,
            requestContext);

        await cacheService.SetAsync(
            BuildAdminTwoFactorCacheKey(tempToken),
            challenge,
            TimeSpan.FromMinutes(AuthConstants.AdminTwoFactorChallengeRetentionMinutes),
            cancellationToken);
        await SendAdminOtpAsync(account.Email, account.FullName, otpCode, cancellationToken);

        return new AdminTwoFactorResponseDto(tempToken, expiresAtUtc);
    }

    private Task SendAdminOtpAsync(
        string email,
        string fullName,
        string otpCode,
        CancellationToken cancellationToken) =>
        emailService.SendAsync(
            email,
            "Your APCS administrator verification code",
            $"Hi {fullName},\n\nYour administrator verification code is: {otpCode}\n\nThis code expires in {AuthConstants.AdminTwoFactorCodeMinutes} minutes. If you did not try to sign in, contact support.",
            cancellationToken);

    private static string BuildAdminTwoFactorCacheKey(string tempToken) =>
        $"{AuthConstants.AdminTwoFactorCacheKeyPrefix}{HashHelper.ComputeSha256Hash(tempToken)}";

    private static string GenerateOtpCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string GenerateOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

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

        if (roles.Any(role => string.Equals(role, AuthConstants.AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            var challenge = await CreateAdminTwoFactorChallengeAsync(
                account,
                request.Context ?? RequestContext.None,
                cancellationToken);

            return Result.Success(new LoginResponseDto(
                null,
                null,
                null,
                null,
                null,
                RequiresTwoFactor: true,
                TempToken: challenge.TempToken,
                TwoFactorExpiresAtUtc: challenge.ExpiresAtUtc));
        }

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
    /// <remarks>
    /// Registration does not issue a session. The account is created pending verification and cannot
    /// authenticate until the emailed verification link is redeemed.
    /// </remarks>
    public async Task<Result<RegisterResponseDto>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<RegisterResponseDto>(validation.ToValidationError());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await accountService.EmailExistsAsync(email, cancellationToken))
        {
            return Result.Failure<RegisterResponseDto>(AuthErrors.EmailAlreadyExists());
        }

        // Optional for callers that never collected one; the local part of the email is a
        // reasonable display name until the user sets their own.
        var fullName = string.IsNullOrWhiteSpace(request.FullName)
            ? email.Split('@')[0]
            : request.FullName.Trim();

        var creation = await CreateAccountAsync(
            email,
            request.Password,
            fullName,
            request.Context ?? RequestContext.None,
            cancellationToken);

        if (creation.IsFailure)
        {
            return Result.Failure<RegisterResponseDto>(creation.Error);
        }

        var (account, verification) = creation.Value;

        // The account exists once the transaction commits, so a delivery failure must not fail
        // registration: the user is told to check their inbox and can request a new link.
        try
        {
            await emailService.SendEmailVerificationAsync(
                account.Email,
                account.FullName,
                verification.Token,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not send the verification email for user {UserId}; the account awaits a resend request",
                account.Id);
        }

        return Result.Success(new RegisterResponseDto(account.Id, account.Email, true));
    }

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

    /// <summary>
    /// Creates the account and its verification token as one atomic change.
    /// </summary>
    private async Task<Result<(AccountInfoDto Account, EmailVerification Verification)>> CreateAccountAsync(
        string email,
        string password,
        string fullName,
        RequestContext requestContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var createResult = await accountService.CreateUserAsync(
                email,
                password,
                fullName,
                cancellationToken);

            if (!createResult.Succeeded || createResult.User is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<(AccountInfoDto, EmailVerification)>(
                    AuthErrors.RegistrationFailed(createResult.Errors));
            }

            var account = createResult.User;
            var verification = EmailVerificationFactory.Create(
                jwtService,
                account.Id,
                timeProvider.GetUtcNow(),
                requestContext);

            await authTokenRepository.AddAsync(verification.Entity, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success((account, verification));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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

    /// <inheritdoc />
    /// <remarks>
    /// Always reports success. An unknown address and an unverified account are both answered
    /// the same way as a genuine request, so the endpoint cannot be used to discover which
    /// addresses are registered.
    /// </remarks>
    public async Task<Result> ForgotPasswordAsync(
        ForgotPasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await forgotPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var account = await accountService.FindByEmailAsync(email, cancellationToken);

        // No account, unverified email, or inactive account status (locked, suspended, deleted) — silently succeed.
        if (account is null || !account.IsEmailVerified || !account.IsActive)
        {
            return Result.Success();
        }

        var utcNow = timeProvider.GetUtcNow();

        // The database keeps at most one redeemable token per user and kind, and expiry does not
        // release that slot. The existing token is therefore revoked before its replacement is
        // issued, which also stops an old link from still working after a new request.
        var outstanding = await authTokenRepository.GetRedeemableAsync(
            account.Id,
            AuthTokenTypes.PasswordReset,
            cancellationToken);

        foreach (var token in outstanding)
        {
            token.Revoke(utcNow);
        }

        var reset = PasswordResetFactory.Create(
            jwtService, account.Id, utcNow, request.Context ?? RequestContext.None);

        await authTokenRepository.AddAsync(reset.Entity, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The token is already persisted, so a delivery failure must not fail the use case:
        // the user can request a new link.
        try
        {
            await emailService.SendPasswordResetAsync(
                account.Email, account.FullName, reset.Token, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not send the password reset email for user {UserId}; the user may request a new link",
                account.Id);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ResetPasswordAsync(
        ResetPasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await resetPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        var tokenHash = jwtService.HashPasswordResetToken(request.Token);
        var token = await authTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        // A verification token would also resolve by hash, so the kind is checked before anything
        // is redeemed: only a password_reset token may change a password.
        if (token is null || token.TokenType != AuthTokenTypes.PasswordReset)
        {
            return Result.Failure(AuthErrors.PasswordResetTokenInvalid());
        }

        var utcNow = timeProvider.GetUtcNow();

        if (token.IsExpired(utcNow))
        {
            return Result.Failure(AuthErrors.PasswordResetTokenExpired());
        }

        if (!token.IsActive(utcNow))
        {
            return Result.Failure(AuthErrors.PasswordResetTokenInvalid());
        }

        var account = await accountService.FindByIdAsync(token.UserId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            return Result.Failure(AuthErrors.PasswordResetTokenInvalid());
        }

        if (!await accountService.UpdatePasswordAsync(account.Id, request.NewPassword, cancellationToken))
        {
            return Result.Failure(AuthErrors.PasswordResetTokenInvalid());
        }

        // Spend the token so it cannot be replayed.
        token.MarkUsed(utcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ChangePasswordAsync(
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await changePasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure(AuthErrors.Unauthenticated());
        }

        var userId = currentUser.UserId.Value;

        if (!await accountService.VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken))
        {
            return Result.Failure(AuthErrors.PasswordIncorrect());
        }

        if (!await accountService.UpdatePasswordAsync(userId, request.NewPassword, cancellationToken))
        {
            return Result.Failure(AuthErrors.UserNotFound());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

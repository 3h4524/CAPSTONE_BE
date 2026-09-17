using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Profile.Common;
using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.Features.Profile.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;

namespace APCS.Application.Features.Profile;

/// <summary>
/// Implements the Profile feature's use cases.
/// </summary>
public sealed class ProfileService(
    ICurrentUser currentUser,
    IAccountService accountService,
    IAccountRepository accountRepository,
    IRepository<UserProfile> userProfileRepository,
    IUnitOfWork unitOfWork,
    IValidator<UpdateProfileRequestDto> updateProfileValidator,
    TimeProvider timeProvider)
    : IProfileService
{
    /// <inheritdoc />
    public async Task<Result<ProfileResponseDto>> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<ProfileResponseDto>(ProfileErrors.Unauthenticated());
        }

        var userId = currentUser.UserId.Value;
        var user = await accountRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ProfileResponseDto>(ProfileErrors.UserNotFound());
        }

        var profile = (await userProfileRepository.FindAsync(
                candidate => candidate.UserId == userId, cancellationToken))
            .FirstOrDefault();

        return Result.Success(Map(user, profile));
    }

    /// <inheritdoc />
    public async Task<Result<ProfileResponseDto>> UpdateProfileAsync(
        UpdateProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await updateProfileValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<ProfileResponseDto>(validation.ToValidationError());
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<ProfileResponseDto>(ProfileErrors.Unauthenticated());
        }

        var userId = currentUser.UserId.Value;
        var user = await accountRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ProfileResponseDto>(ProfileErrors.UserNotFound());
        }

        if (request.Email is not null)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var currentEmail = user.Email.Trim().ToLowerInvariant();
            if (!string.Equals(normalizedEmail, currentEmail, StringComparison.Ordinal))
            {
                if (await accountService.EmailExistsAsync(normalizedEmail, cancellationToken))
                {
                    var owner = await accountService.FindByEmailAsync(normalizedEmail, cancellationToken);
                    if (owner is null || owner.Id != userId)
                    {
                        return Result.Failure<ProfileResponseDto>(ProfileErrors.EmailAlreadyExists());
                    }
                }
            }
        }

        var profile = (await userProfileRepository.FindAsync(
                candidate => candidate.UserId == userId, cancellationToken))
            .FirstOrDefault();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (profile is null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Timezone = ProfileOptions.DefaultTimezone,
                Language = ProfileOptions.DefaultLanguage,
                ThemePreference = ProfileOptions.DefaultThemePreference,
                CreatedAt = now
            };
            await userProfileRepository.AddAsync(profile, cancellationToken: cancellationToken);
        }

        Apply(request, user, profile);
        user.UpdatedAt = now;
        profile.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Map(user, profile));
    }

    private static void Apply(UpdateProfileRequestDto request, User user, UserProfile profile)
    {
        if (request.FullName is not null)
        {
            user.FullName = request.FullName.Trim();
        }

        if (request.Email is not null)
        {
            user.Email = request.Email.Trim().ToLowerInvariant();
        }

        if (request.ShopName is not null)
        {
            var shopName = request.ShopName.Trim();
            profile.ShopName = shopName.Length == 0 ? null : shopName;
        }

        if (request.ShopDescription is not null)
        {
            var shopDescription = request.ShopDescription.Trim();
            profile.ShopDescription = shopDescription.Length == 0 ? null : shopDescription;
        }

        if (request.Timezone is not null)
        {
            profile.Timezone = request.Timezone.Trim();
        }

        if (request.Language is not null)
        {
            profile.Language = request.Language.Trim();
        }

        if (request.ThemePreference is not null)
        {
            var themePreference = request.ThemePreference.Trim();
            profile.ThemePreference = themePreference.Length == 0 ? null : themePreference;
        }

        if (request.NotificationEmailEnabled.HasValue)
        {
            profile.NotificationEmailEnabled = request.NotificationEmailEnabled.Value;
        }

        if (request.NewsletterSubscribed.HasValue)
        {
            profile.NewsletterSubscribed = request.NewsletterSubscribed.Value;
        }

        if (request.TwoFactorEnabled.HasValue)
        {
            profile.TwoFactorEnabled = request.TwoFactorEnabled.Value;
        }
    }

    private static ProfileResponseDto Map(User user, UserProfile? profile) =>
        new(
            user.FullName,
            user.Email,
            user.AvatarUrl,
            profile?.ShopName,
            profile?.ShopDescription,
            profile?.Timezone ?? ProfileOptions.DefaultTimezone,
            profile?.Language ?? ProfileOptions.DefaultLanguage,
            profile?.ThemePreference ?? ProfileOptions.DefaultThemePreference,
            profile?.NotificationEmailEnabled ?? false,
            profile?.NewsletterSubscribed ?? false,
            profile?.TwoFactorEnabled ?? false);
}

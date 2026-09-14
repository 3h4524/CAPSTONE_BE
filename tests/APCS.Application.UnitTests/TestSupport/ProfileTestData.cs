using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Profile;
using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.TestSupport;

internal static class ProfileTestData
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly DateTimeOffset UtcNow = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider CreateTimeProvider() => new(UtcNow);

    public static User CreateUser(
        string email = "user@example.com",
        string fullName = "User Name",
        string? avatarUrl = "https://example.com/avatar.png") => new()
        {
            Id = UserId,
            Email = email,
            FullName = fullName,
            AvatarUrl = avatarUrl,
            AccountStatus = "Active"
        };

    public static UserProfile CreateProfile(Guid? userId = null) => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UserId = userId ?? UserId,
        ShopName = "Shop Name",
        ShopDescription = "Shop Description",
        Timezone = "Asia/Ho_Chi_Minh",
        Language = "vi",
        ThemePreference = "system",
        NotificationEmailEnabled = true,
        NewsletterSubscribed = false,
        TwoFactorEnabled = false
    };

    public static Mock<ICurrentUser> CreateAuthenticatedUser(Guid? userId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.UserId).Returns(userId ?? UserId);
        return currentUser;
    }

    /// <summary>
    /// Creates a validator mock that reports every request as valid, so service tests exercise
    /// business logic rather than the FluentValidation rules covered by validator tests.
    /// </summary>
    public static Mock<IValidator<UpdateProfileRequestDto>> CreatePassingValidator()
    {
        var validator = new Mock<IValidator<UpdateProfileRequestDto>>();
        validator.Setup(candidate => candidate.ValidateAsync(It.IsAny<UpdateProfileRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return validator;
    }

    /// <summary>
    /// Builds a <see cref="ProfileService"/> with loose mocks for every dependency a test
    /// does not override, including a validator that always passes.
    /// </summary>
    public static ProfileService CreateService(
        Mock<ICurrentUser>? currentUser = null,
        Mock<IAccountService>? accountService = null,
        Mock<IAccountRepository>? accountRepository = null,
        Mock<IRepository<UserProfile>>? userProfileRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IValidator<UpdateProfileRequestDto>>? validator = null,
        TimeProvider? timeProvider = null) =>
        new(
            (currentUser ?? CreateAuthenticatedUser()).Object,
            (accountService ?? new Mock<IAccountService>()).Object,
            (accountRepository ?? new Mock<IAccountRepository>()).Object,
            (userProfileRepository ?? new Mock<IRepository<UserProfile>>()).Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            (validator ?? CreatePassingValidator()).Object,
            timeProvider ?? CreateTimeProvider());
}

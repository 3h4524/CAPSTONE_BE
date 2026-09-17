using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Profile.Common;
using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;

namespace APCS.Application.UnitTests.Features.Profile.ProfileServiceTests;

[TestClass]
public sealed class UpdateProfileTests
{
    [TestMethod]
    public async Task UpdateProfileAsync_WhenUserIsNotAuthenticated_ReturnsUnauthenticated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var service = ProfileTestData.CreateService(currentUser: currentUser);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(FullName: "Seller Name"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenValidationFails_ReturnsValidationError()
    {
        var validator = new Mock<IValidator<UpdateProfileRequestDto>>();
        validator.Setup(candidate => candidate.ValidateAsync(
                It.IsAny<UpdateProfileRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("FullName", "Full name is required.")
            }));
        var service = ProfileTestData.CreateService(validator: validator);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(FullName: ""),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenEmailBelongsToAnotherUser_ReturnsEmailAlreadyExists()
    {
        var ct = new CancellationTokenSource().Token;
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(ProfileTestData.UserId, ct))
            .ReturnsAsync(ProfileTestData.CreateUser());
        var accountService = new Mock<IAccountService>();
        accountService.Setup(svc => svc.EmailExistsAsync("taken@example.com", ct))
            .ReturnsAsync(true);
        accountService.Setup(svc => svc.FindByEmailAsync("taken@example.com", ct))
            .ReturnsAsync(new AccountInfoDto(Guid.NewGuid(), "taken@example.com", "Other", true, true));
        var service = ProfileTestData.CreateService(
            accountService: accountService,
            accountRepository: accountRepository);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(Email: "taken@example.com"), ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.EmailAlreadyExists);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenEmailIsOwnAddress_UpdatesAndSavesOnce()
    {
        var ct = new CancellationTokenSource().Token;
        var user = ProfileTestData.CreateUser();
        var profile = ProfileTestData.CreateProfile();
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(ProfileTestData.UserId, ct))
            .ReturnsAsync(user);
        var accountService = new Mock<IAccountService>();
        var userProfileRepository = new Mock<IRepository<UserProfile>>();
        userProfileRepository.Setup(repository => repository.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserProfile, bool>>>(), ct))
            .ReturnsAsync(new List<UserProfile> { profile });
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = ProfileTestData.CreateService(
            accountService: accountService,
            accountRepository: accountRepository,
            userProfileRepository: userProfileRepository,
            unitOfWork: unitOfWork);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(Email: "USER@example.com"), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("user@example.com");
        accountService.Verify(svc => svc.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(ct), Times.Once);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenRequestIsValid_UpdatesUserAndProfileInOneSave()
    {
        var ct = new CancellationTokenSource().Token;
        var user = ProfileTestData.CreateUser();
        var profile = ProfileTestData.CreateProfile();
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(ProfileTestData.UserId, ct))
            .ReturnsAsync(user);
        var userProfileRepository = new Mock<IRepository<UserProfile>>();
        userProfileRepository.Setup(repository => repository.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserProfile, bool>>>(), ct))
            .ReturnsAsync(new List<UserProfile> { profile });
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = ProfileTestData.CreateService(
            accountRepository: accountRepository,
            userProfileRepository: userProfileRepository,
            unitOfWork: unitOfWork,
            timeProvider: ProfileTestData.CreateTimeProvider());

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(
                FullName: "  New Name  ",
                ShopName: "New Shop",
                Timezone: "UTC",
                Language: "en",
                NewsletterSubscribed: true),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.FullName.Should().Be("New Name");
        result.Value.ShopName.Should().Be("New Shop");
        result.Value.Timezone.Should().Be("UTC");
        result.Value.Language.Should().Be("en");
        result.Value.NewsletterSubscribed.Should().BeTrue();
        user.FullName.Should().Be("New Name");
        profile.ShopName.Should().Be("New Shop");
        user.UpdatedAt.Should().Be(ProfileTestData.UtcNow.UtcDateTime);
        profile.UpdatedAt.Should().Be(ProfileTestData.UtcNow.UtcDateTime);
        unitOfWork.Verify(uow => uow.SaveChangesAsync(ct), Times.Once);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenUserNotFound_ReturnsUserNotFound()
    {
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var service = ProfileTestData.CreateService(accountRepository: accountRepository);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(FullName: "Seller Name"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task UpdateProfileAsync_WhenProfileIsMissing_CreatesProfileWithApplicationDefaults()
    {
        var ct = new CancellationTokenSource().Token;
        var user = ProfileTestData.CreateUser();
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(ProfileTestData.UserId, ct))
            .ReturnsAsync(user);
        var userProfileRepository = new Mock<IRepository<UserProfile>>();
        userProfileRepository.Setup(repository => repository.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserProfile, bool>>>(), ct))
            .ReturnsAsync(new List<UserProfile>());
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = ProfileTestData.CreateService(
            accountRepository: accountRepository,
            userProfileRepository: userProfileRepository,
            unitOfWork: unitOfWork);

        var result = await service.UpdateProfileAsync(
            new UpdateProfileRequestDto(ShopName: "New Shop"), ct);

        result.IsSuccess.Should().BeTrue();
        userProfileRepository.Verify(repository => repository.AddAsync(
            It.Is<UserProfile>(candidate =>
                candidate.UserId == ProfileTestData.UserId
                && candidate.Timezone == ProfileOptions.DefaultTimezone
                && candidate.Language == ProfileOptions.DefaultLanguage
                && candidate.ThemePreference == ProfileOptions.DefaultThemePreference),
            false,
            ct), Times.Once);
    }
}

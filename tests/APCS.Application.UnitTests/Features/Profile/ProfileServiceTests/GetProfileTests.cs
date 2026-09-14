using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Profile.ProfileServiceTests;

[TestClass]
public sealed class GetProfileTests
{
    [TestMethod]
    public async Task GetProfileAsync_WhenUserIsNotAuthenticated_ReturnsUnauthenticated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var service = ProfileTestData.CreateService(currentUser: currentUser);

        var result = await service.GetProfileAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task GetProfileAsync_WhenUserNotFound_ReturnsUserNotFound()
    {
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(
                It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var service = ProfileTestData.CreateService(accountRepository: accountRepository);

        var result = await service.GetProfileAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task GetProfileAsync_WhenProfileIsMissing_ReturnsUserFieldsWithDefaults()
    {
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(
                ProfileTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileTestData.CreateUser());
        var userProfileRepository = new Mock<IRepository<UserProfile>>();
        userProfileRepository.Setup(repository => repository.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserProfile, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile>());
        var service = ProfileTestData.CreateService(
            accountRepository: accountRepository,
            userProfileRepository: userProfileRepository);

        var result = await service.GetProfileAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FullName.Should().Be("User Name");
        result.Value.Email.Should().Be("user@example.com");
        result.Value.AvatarUrl.Should().Be("https://example.com/avatar.png");
        result.Value.Timezone.Should().Be("Asia/Ho_Chi_Minh");
        result.Value.Language.Should().Be("vi");
    }

    [TestMethod]
    public async Task GetProfileAsync_WhenProfileExists_ReturnsMappedProfile()
    {
        var accountRepository = new Mock<IAccountRepository>();
        accountRepository.Setup(repository => repository.GetByIdAsync(
                ProfileTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileTestData.CreateUser());
        var userProfileRepository = new Mock<IRepository<UserProfile>>();
        userProfileRepository.Setup(repository => repository.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<System.Func<UserProfile, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile> { ProfileTestData.CreateProfile() });
        var service = ProfileTestData.CreateService(
            accountRepository: accountRepository,
            userProfileRepository: userProfileRepository);

        var result = await service.GetProfileAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShopName.Should().Be("Shop Name");
        result.Value.ShopDescription.Should().Be("Shop Description");
        result.Value.NotificationEmailEnabled.Should().BeTrue();
        result.Value.NewsletterSubscribed.Should().BeFalse();
    }
}

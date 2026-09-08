using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class GetCurrentUserTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task GetCurrentUserAsync_WhenCurrentUserIsInvalid_ReturnsUnauthorized(
        bool isAuthenticated,
        bool hasUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(user => user.UserId).Returns(hasUserId ? AuthTestData.ActiveUser.Id : null);
        var account = new Mock<IAccountService>();
        var service = AuthTestData.CreateService(account, currentUser: currentUser);

        var result = await service.GetCurrentUserAsync(CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
        account.Verify(service => service.FindByIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetCurrentUserAsync_WhenAccountDoesNotExist_ReturnsNotFound()
    {
        var currentUser = CreateAuthenticatedCurrentUser();
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);
        var service = AuthTestData.CreateService(account, currentUser: currentUser);

        var result = await service.GetCurrentUserAsync(CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task GetCurrentUserAsync_WhenAuthenticated_ReturnsMappedUserAndRoles()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var currentUser = CreateAuthenticatedCurrentUser();
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var service = AuthTestData.CreateService(account, currentUser: currentUser);

        var result = await service.GetCurrentUserAsync(cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(AuthTestData.ActiveUser.Id);
        result.Value.Email.Should().Be(AuthTestData.ActiveUser.Email);
        result.Value.FullName.Should().Be(AuthTestData.ActiveUser.FullName);
        result.Value.Roles.Should().Equal(AuthTestData.Roles);
    }

    private static Mock<ICurrentUser> CreateAuthenticatedCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(AuthTestData.ActiveUser.Id);
        return currentUser;
    }
}

using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Queries.GetCurrentUser;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Auth.Queries.GetCurrentUser;

[TestClass]
public sealed class GetCurrentUserQueryHandlerTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Handle_WhenCurrentUserIsInvalid_ReturnsUnauthorized(
        bool isAuthenticated,
        bool hasUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(user => user.UserId).Returns(hasUserId ? AuthTestData.ActiveUser.Id : null);
        var account = new Mock<IAccountService>();
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, account.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
        account.Verify(service => service.FindByIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenAccountDoesNotExist_ReturnsNotFound()
    {
        var currentUser = CreateAuthenticatedCurrentUser();
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, account.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task Handle_WhenAuthenticated_ReturnsMappedUserAndRoles()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var currentUser = CreateAuthenticatedCurrentUser();
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, account.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), cancellationToken);

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

using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Features.Auth.Common;
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
    [DataRow(false, null)]
    [DataRow(true, null)]
    [DataRow(false, 42)]
    public async Task Handle_WhenCurrentUserIsInvalid_ReturnsUnauthorized(
        bool isAuthenticated,
        int? userId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        var identity = new Mock<IIdentityService>();
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, identity.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
        identity.Verify(service => service.FindByIdAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenIdentityUserDoesNotExist_ReturnsNotFound()
    {
        var currentUser = CreateAuthenticatedCurrentUser();
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, identity.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserNotFound);
    }

    [TestMethod]
    public async Task Handle_WhenAuthenticated_ReturnsMappedUserAndRoles()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var currentUser = CreateAuthenticatedCurrentUser();
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(42, cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        identity.Setup(service => service.GetRolesAsync(42, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var handler = new GetCurrentUserQueryHandler(currentUser.Object, identity.Object);

        var result = await handler.Handle(new GetCurrentUserQuery(), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(42);
        result.Value.Email.Should().Be(AuthTestData.ActiveUser.Email);
        result.Value.FullName.Should().Be(AuthTestData.ActiveUser.FullName);
        result.Value.Roles.Should().Equal(AuthTestData.Roles);
    }

    private static Mock<ICurrentUser> CreateAuthenticatedCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(42);
        return currentUser;
    }
}

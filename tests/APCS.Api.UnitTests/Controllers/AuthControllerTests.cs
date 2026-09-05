using APCS.Api.Controllers;
using APCS.Application.Features.Auth.Commands.Login;
using APCS.Application.Features.Auth.Commands.Logout;
using APCS.Application.Features.Auth.Commands.RefreshToken;
using APCS.Application.Features.Auth.Commands.Register;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Queries.GetCurrentUser;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AuthControllerTests
{
    // A DefaultHttpContext exposes no remote address and no user agent, so this is what the
    // controller captures under test.
    private static readonly RequestContext ExpectedContext = new(null, null);

    private static readonly DateTimeOffset AccessExpiresAt =
        new(2026, 8, 31, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RefreshExpiresAt =
        new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Register_WhenSuccessful_SendsCommandAndSetsSecureCookie()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(It.IsAny<RegisterCommand>(), cancellationToken))
            .ReturnsAsync(Result.Success(CreateRegisterResponse()));
        var controller = CreateController(sender);

        var command = new RegisterCommand("seller@example.com", "Password1", "Seller Name");

        var action = await controller.Register(command, cancellationToken);

        action.Should().BeOfType<OkObjectResult>();

        // The controller stamps the caller's network details onto the command before dispatching.
        var expected = command with { Context = ExpectedContext };
        sender.Verify(candidate => candidate.Send(expected, cancellationToken), Times.Once);
        AssertRefreshCookie(controller, "refresh-token");
    }

    [TestMethod]
    public async Task Register_WhenFailed_ReturnsProblemWithoutSettingCookie()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(
                It.IsAny<RegisterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RegisterResponse>(
                Error.Conflict(ErrorCodes.EmailAlreadyExists, "Already exists")));
        var controller = CreateController(sender);

        var action = await controller.Register(
            new RegisterCommand("seller@example.com", "Password1", "Seller"),
            CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Login_WhenSuccessful_SetsCookieAndReturnsResponse()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateLoginResponse()));
        var controller = CreateController(sender);

        var command = new LoginCommand("seller@example.com", "Password1");

        var action = await controller.Login(command, CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>();
        var expected = command with { Context = ExpectedContext };
        sender.Verify(candidate => candidate.Send(expected, It.IsAny<CancellationToken>()), Times.Once);
        AssertRefreshCookie(controller, "refresh-token");
    }

    [TestMethod]
    public async Task Refresh_WhenSuccessful_ReadsOldCookieAndSetsRotatedCookie()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(
                It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RefreshTokenResponse(
                "new-access", AccessExpiresAt, "new-refresh", RefreshExpiresAt)));
        var controller = CreateController(sender, "old-refresh");

        var action = await controller.Refresh(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>();
        sender.Verify(candidate => candidate.Send(
            It.Is<RefreshTokenCommand>(command =>
                command.RefreshToken == "old-refresh"),
            It.IsAny<CancellationToken>()), Times.Once);
        AssertRefreshCookie(controller, "new-refresh");
    }

    [TestMethod]
    public async Task Refresh_WhenFailed_ClearsCookieAndReturnsProblem()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(
                It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RefreshTokenResponse>(
                Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "Invalid")));
        var controller = CreateController(sender, "old-refresh");

        var action = await controller.Refresh(CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        AssertCookieWasCleared(controller);
    }

    [TestMethod]
    public async Task Logout_WhenSuccessful_SendsCookieClearsItAndReturnsNoContent()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(
                It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var controller = CreateController(sender, "refresh-token");

        var action = await controller.Logout(CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();
        sender.Verify(candidate => candidate.Send(
            It.Is<LogoutCommand>(command =>
                command.RefreshToken == "refresh-token"),
            It.IsAny<CancellationToken>()), Times.Once);
        AssertCookieWasCleared(controller);
    }

    [TestMethod]
    public async Task Me_WhenSuccessful_ReturnsCurrentUser()
    {
        var sender = new Mock<ISender>();
        sender.Setup(candidate => candidate.Send(
                It.IsAny<GetCurrentUserQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateUser()));
        var controller = CreateController(sender);

        var action = await controller.Me(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(CreateUser());
    }

    private static AuthController CreateController(Mock<ISender> sender, string? refreshCookie = null)
    {
        var context = new DefaultHttpContext();
        if (refreshCookie is not null)
        {
            context.Request.Headers.Cookie = $"{AuthConstants.RefreshTokenCookieName}={refreshCookie}";
        }

        return new AuthController(sender.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static void AssertRefreshCookie(AuthController controller, string value)
    {
        var header = controller.Response.Headers.SetCookie.ToString();
        header.Should().Contain($"{AuthConstants.RefreshTokenCookieName}={value}");
        var normalized = header.ToLowerInvariant();
        normalized.Should().Contain("httponly", Exactly.Once());
        normalized.Should().Contain("secure", Exactly.Once());
        normalized.Should().Contain("samesite=lax", Exactly.Once());
        normalized.Should().Contain("path=/", Exactly.Once());
    }

    private static void AssertCookieWasCleared(AuthController controller)
    {
        var header = controller.Response.Headers.SetCookie.ToString();
        header.Should().Contain($"{AuthConstants.RefreshTokenCookieName}=");
        var normalized = header.ToLowerInvariant();
        normalized.Should().Contain("expires=");
        normalized.Should().Contain("secure");
        normalized.Should().Contain("samesite=lax");
        normalized.Should().Contain("path=/");
    }

    private static RegisterResponse CreateRegisterResponse() => new(
        "access-token", AccessExpiresAt, "refresh-token", RefreshExpiresAt, CreateUser());

    private static LoginResponse CreateLoginResponse() => new(
        "access-token", AccessExpiresAt, "refresh-token", RefreshExpiresAt, CreateUser());

    private static AuthenticatedUserResponse CreateUser() =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "user@example.com", "User Name", ["Seller"]);
}

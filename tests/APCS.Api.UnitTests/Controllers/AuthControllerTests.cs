using APCS.Api.Controllers;
using APCS.Application.Features.Auth;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
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
    public async Task Login_WhenSuccessful_SetsCookieAndReturnsResponse()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(candidate => candidate.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateLoginResponse()));
        var controller = CreateController(authService);

        var request = new LoginRequestDto("seller@example.com", "Password1");

        var action = await controller.Login(request, CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>();
        var expected = request with { Context = ExpectedContext };
        authService.Verify(
            candidate => candidate.LoginAsync(expected, It.IsAny<CancellationToken>()),
            Times.Once);
        AssertRefreshCookie(controller, "refresh-token");
    }

    [TestMethod]
    public async Task Refresh_WhenSuccessful_ReadsOldCookieAndSetsRotatedCookie()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(candidate => candidate.RefreshTokenAsync(
                It.IsAny<string?>(), It.IsAny<RequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RefreshTokenResponseDto(
                "new-access", AccessExpiresAt, "new-refresh", RefreshExpiresAt)));
        var controller = CreateController(authService, "old-refresh");

        var action = await controller.Refresh(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>();
        authService.Verify(candidate => candidate.RefreshTokenAsync(
            "old-refresh", It.IsAny<RequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
        AssertRefreshCookie(controller, "new-refresh");
    }

    [TestMethod]
    public async Task Refresh_WhenFailed_ClearsCookieAndReturnsProblem()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(candidate => candidate.RefreshTokenAsync(
                It.IsAny<string?>(), It.IsAny<RequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RefreshTokenResponseDto>(
                Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "Invalid")));
        var controller = CreateController(authService, "old-refresh");

        var action = await controller.Refresh(CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        AssertCookieWasCleared(controller);
    }

    [TestMethod]
    public async Task Logout_WhenSuccessful_SendsCookieClearsItAndReturnsNoContent()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(candidate => candidate.LogoutAsync(
                It.IsAny<string?>(), It.IsAny<RequestContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var controller = CreateController(authService, "refresh-token");

        var action = await controller.Logout(CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();
        authService.Verify(candidate => candidate.LogoutAsync(
            "refresh-token", It.IsAny<RequestContext?>(), It.IsAny<CancellationToken>()), Times.Once);
        AssertCookieWasCleared(controller);
    }

    [TestMethod]
    public async Task Me_WhenSuccessful_ReturnsCurrentUser()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(candidate => candidate.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateUser()));
        var controller = CreateController(authService);

        var action = await controller.Me(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(CreateUser());
    }

    private static AuthController CreateController(Mock<IAuthService> authService, string? refreshCookie = null)
    {
        var context = new DefaultHttpContext();
        if (refreshCookie is not null)
        {
            context.Request.Headers.Cookie = $"{AuthConstants.RefreshTokenCookieName}={refreshCookie}";
        }

        return new AuthController(authService.Object)
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

    private static LoginResponseDto CreateLoginResponse() => new(
        "access-token", AccessExpiresAt, "refresh-token", RefreshExpiresAt, CreateUser());

    private static AuthenticatedUserResponse CreateUser() =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "user@example.com", "User Name", ["Seller"]);
}

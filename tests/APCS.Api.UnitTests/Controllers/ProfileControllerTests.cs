using APCS.Api.Controllers;
using APCS.Application.Features.Profile;
using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.Features.Profile.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class ProfileControllerTests
{
    [TestMethod]
    public async Task Get_WhenSuccessful_ReturnsOkWithProfile()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateProfile()));
        var controller = CreateController(profileService);

        var action = await controller.Get(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(CreateProfile());
        profileService.Verify(candidate => candidate.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Get_WhenUserNotFound_ReturnsNotFoundProblem()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProfileResponseDto>(
                Error.NotFound(ErrorCodes.UserNotFound, "User was not found.")));
        var controller = CreateController(profileService);

        var action = await controller.Get(CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Patch_WhenSuccessful_ReturnsOkWithProfile()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.UpdateProfileAsync(
                It.IsAny<UpdateProfileRequestDto>(), cancellationToken))
            .ReturnsAsync(Result.Success(CreateProfile()));
        var controller = CreateController(profileService);

        var request = new UpdateProfileRequestDto(FullName: "New Name");

        var action = await controller.Patch(request, cancellationToken);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(CreateProfile());
        profileService.Verify(
            candidate => candidate.UpdateProfileAsync(request, cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Patch_WhenEmailIsTaken_ReturnsConflictProblem()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.UpdateProfileAsync(
                It.IsAny<UpdateProfileRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProfileResponseDto>(
                Error.Conflict(ErrorCodes.EmailAlreadyExists, "Email is already registered.")));
        var controller = CreateController(profileService);

        var action = await controller.Patch(
            new UpdateProfileRequestDto(Email: "taken@example.com"),
            CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
    }

    [TestMethod]
    public async Task Get_WhenUnauthenticated_ReturnsUnauthorizedProblem()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProfileResponseDto>(
                Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.")));
        var controller = CreateController(profileService);

        var action = await controller.Get(CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
    }

    [TestMethod]
    public async Task Patch_WhenValidationFails_ReturnsBadRequestProblem()
    {
        var profileService = new Mock<IProfileService>();
        profileService.Setup(candidate => candidate.UpdateProfileAsync(
                It.IsAny<UpdateProfileRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProfileResponseDto>(
                Error.Validation(
                    "Full name is required.",
                    new Dictionary<string, string[]> { ["FullName"] = ["Full name is required."] })));
        var controller = CreateController(profileService);

        var action = await controller.Patch(
            new UpdateProfileRequestDto(FullName: ""),
            CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    private static ProfileController CreateController(Mock<IProfileService> profileService) =>
        new(profileService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ProfileResponseDto CreateProfile() => new(
        "User Name",
        "user@example.com",
        "https://example.com/avatar.png",
        "Shop Name",
        "Shop Description",
        "Asia/Ho_Chi_Minh",
        "vi",
        "system",
        true,
        false,
        false);
}

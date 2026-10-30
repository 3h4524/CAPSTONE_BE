using APCS.Api.Controllers;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class VideoQualityControllerTests
{
    [TestMethod]
    public async Task Evaluate_ForwardsVideoSceneAndCancellation_ReturnsQualityPayload()
    {
        using var cancellation = new CancellationTokenSource();
        var runId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var response = new VideoSceneQualityResponse(videoId, 0, null, []);
        var service = new Mock<IVideoQualityReviewService>();
        service.Setup(item => item.EvaluateAsync(runId, videoId, 0, cancellation.Token)).ReturnsAsync(Result.Success(response));

        var action = await new VideoQualityController(service.Object).Evaluate(runId, videoId, 0, cancellation.Token);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public void Controller_RequiresSellerRole()
    {
        typeof(VideoQualityController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("Seller");
    }
}

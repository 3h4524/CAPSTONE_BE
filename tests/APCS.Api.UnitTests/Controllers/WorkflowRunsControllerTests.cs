using APCS.Api.Controllers;
using APCS.Api.UnitTests.TestSupport;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class WorkflowRunsControllerTests
{
    [TestMethod]
    public async Task GetAndActions_ForwardRunRevisionAndCancellation_ReturnRunState()
    {
        var run = VideoWorkflowTestData.Run(); var request = new RunActionRequest(3, "cancel");
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IWorkflowRunService>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(run.Id, cancellation.Token)).ReturnsAsync(Result.Success(run));
        service.Setup(x => x.ActionAsync(run.Id, request, cancellation.Token)).ReturnsAsync(Result.Success(run));
        var controller = new WorkflowRunsController(service.Object, Mock.Of<IVideoArtifactService>());
        (await controller.Get(run.Id, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(run);
        (await controller.Action(run.Id, request, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(run);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Action_StaleExactApproval_ReturnsConflict()
    {
        var service = new Mock<IWorkflowRunService>();
        service.Setup(x => x.ActionAsync(It.IsAny<Guid>(), It.IsAny<RunActionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunResponse>(Error.Conflict("StaleRevision", "Reload before continuing.")));
        var controller = new WorkflowRunsController(service.Object, Mock.Of<IVideoArtifactService>());
        var result = (await controller.Action(Guid.NewGuid(), new(1, "approve_video", Guid.NewGuid(), 1), default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(409);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("StaleRevision");
    }

    [TestMethod]
    public async Task Videos_ForwardRunAndCancellation_ReturnsVersionCollection()
    {
        var id = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        IReadOnlyList<VideoResponse> videos = [];
        var service = new Mock<IVideoArtifactService>(MockBehavior.Strict);
        service.Setup(x => x.ListAsync(id, cancellation.Token)).ReturnsAsync(Result.Success(videos));
        var result = await new WorkflowRunsController(Mock.Of<IWorkflowRunService>(), service.Object).Videos(id, cancellation.Token);
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(videos);
        service.VerifyAll();
    }
}

using APCS.Api.Controllers;
using APCS.Api.UnitTests.TestSupport;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class WorkflowsControllerTests
{
    [TestMethod]
    public async Task Create_Success_ReturnsCreatedLocationAndForwardsRequestAndCancellation()
    {
        var workflow = VideoWorkflowTestData.Workflow(); var request = VideoWorkflowTestData.SaveRequest();
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IWorkflowService>(MockBehavior.Strict);
        service.Setup(x => x.SaveAsync(null, request, cancellation.Token)).ReturnsAsync(Result.Success(workflow));
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowRunService>(), new());
        var result = (await controller.Create(request, cancellation.Token)).Should().BeOfType<CreatedAtActionResult>().Subject;
        result.StatusCode.Should().Be(201);
        result.ActionName.Should().Be(nameof(WorkflowsController.Get));
        result.RouteValues!["id"].Should().Be(workflow.Id);
        result.Value.Should().BeSameAs(workflow);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Create_DuplicateName_ReturnsConflictProblem()
    {
        var request = VideoWorkflowTestData.SaveRequest();
        var service = new Mock<IWorkflowService>();
        service.Setup(x => x.SaveAsync(null, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WorkflowResponse>(Error.Conflict("DuplicateWorkflowName", "Choose another name.")));
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowRunService>(), new());
        var result = (await controller.Create(request, default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(409);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("DuplicateWorkflowName");
    }

    [TestMethod]
    public async Task Run_Success_ReturnsAcceptedPollLocation()
    {
        var run = VideoWorkflowTestData.Run(); var workflowId = Guid.NewGuid(); var request = new StartRunRequest(1, "run-key");
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IWorkflowRunService>(MockBehavior.Strict);
        service.Setup(x => x.StartAsync(workflowId, request, cancellation.Token)).ReturnsAsync(Result.Success(run));
        var controller = new WorkflowsController(Mock.Of<IWorkflowService>(), service.Object, new());
        var result = (await controller.Run(workflowId, request, cancellation.Token)).Should().BeOfType<AcceptedResult>().Subject;
        result.StatusCode.Should().Be(202);
        result.Location.Should().Be($"/api/workflow-runs/{run.Id}");
        result.Value.Should().BeSameAs(run);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Run_DisabledMode_ReturnsBadRequestWithStableCode()
    {
        var service = new Mock<IWorkflowRunService>();
        service.Setup(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunResponse>(new("UnsupportedVideoMode", "Only Standard is enabled.", ErrorType.Validation)));
        var controller = new WorkflowsController(Mock.Of<IWorkflowService>(), service.Object, new());
        var result = (await controller.Run(Guid.NewGuid(), new(1, "run-key"), default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(400);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("UnsupportedVideoMode");
    }

    [TestMethod]
    public async Task ReadsAndMutations_ForwardIdentityRevisionAndCancellation_UseExpectedStatuses()
    {
        var workflow = VideoWorkflowTestData.Workflow(); var request = VideoWorkflowTestData.SaveRequest();
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IWorkflowService>(MockBehavior.Strict);
        service.Setup(x => x.ListAsync(cancellation.Token)).ReturnsAsync(Result.Success<IReadOnlyList<WorkflowResponse>>([workflow]));
        service.Setup(x => x.GetAsync(workflow.Id, cancellation.Token)).ReturnsAsync(Result.Success(workflow));
        service.Setup(x => x.SaveAsync(workflow.Id, request, cancellation.Token)).ReturnsAsync(Result.Success(workflow));
        service.Setup(x => x.DeleteAsync(workflow.Id, 4, cancellation.Token)).ReturnsAsync(Result.Success());
        var controller = new WorkflowsController(service.Object, Mock.Of<IWorkflowRunService>(), new());
        (await controller.List(cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<IReadOnlyList<WorkflowResponse>>().Which.Should().ContainSingle(x => x.Id == workflow.Id);
        (await controller.Get(workflow.Id, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(workflow);
        (await controller.Save(workflow.Id, request, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(workflow);
        (await controller.Delete(workflow.Id, 4, cancellation.Token)).Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public void Capabilities_ReturnsBackendOwnedModeAvailability()
    {
        var result = new WorkflowsController(Mock.Of<IWorkflowService>(), Mock.Of<IWorkflowRunService>(), new()).Capabilities();
        var capability = result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<WorkflowCapabilities>().Subject;
        capability.VideoModes.Single(x => x.Mode == "standard").Enabled.Should().BeTrue();
        capability.VideoModes.Where(x => x.Mode != "standard").Should().HaveCount(2).And.OnlyContain(x => !x.Enabled && x.Availability == "coming_soon");
    }
}

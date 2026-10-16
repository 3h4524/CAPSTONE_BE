using APCS.Api.Controllers;
using APCS.Api.Filters;
using APCS.Api.UnitTests.TestSupport;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class InternalMediaJobsControllerTests
{
    [TestMethod]
    public async Task Claim_QueueFailure_ReturnsProblemInsteadOfAccessingAbsentSuccessValue()
    {
        var service = new Mock<IMediaJobService>();
        service.Setup(x => x.ClaimAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WorkerJob?>(Error.Failure("QueueUnavailable", "Queue is unavailable.")));
        var result = (await new InternalMediaJobsController(service.Object).Claim(default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(500);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("QueueUnavailable");
    }

    [TestMethod]
    public async Task Claim_EmptyQueue_ReturnsNoContent()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IMediaJobService>(MockBehavior.Strict);
        service.Setup(x => x.ClaimAsync(cancellation.Token)).ReturnsAsync(Result.Success<WorkerJob?>(null));
        var result = await new InternalMediaJobsController(service.Object).Claim(cancellation.Token);
        result.Should().BeOfType<NoContentResult>().Which.StatusCode.Should().Be(204);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Claim_AvailableJob_ReturnsWorkManifest()
    {
        var job = VideoWorkflowTestData.Job();
        var service = new Mock<IMediaJobService>();
        service.Setup(x => x.ClaimAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<WorkerJob?>(job));
        var result = await new InternalMediaJobsController(service.Object).Claim(default);
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(job);
    }

    [TestMethod]
    public async Task WorkerUpdates_ForwardLeaseRequestsAndCancellation_ReturnNoContent()
    {
        var id = Guid.NewGuid(); var token = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        var heartbeat = new JobHeartbeat(token, "Rendering", 40);
        var completion = new JobCompletion(token, "private/video", "1", "private/thumbnail", "1", null);
        var failure = new JobFailure(token, "Failed processing");
        var service = new Mock<IMediaJobService>(MockBehavior.Strict);
        service.Setup(x => x.HeartbeatAsync(id, heartbeat, cancellation.Token)).ReturnsAsync(Result.Success());
        service.Setup(x => x.CompleteAsync(id, completion, cancellation.Token)).ReturnsAsync(Result.Success());
        service.Setup(x => x.FailAsync(id, failure, cancellation.Token)).ReturnsAsync(Result.Success());
        var controller = new InternalMediaJobsController(service.Object);
        (await controller.Heartbeat(id, heartbeat, cancellation.Token)).Should().BeOfType<NoContentResult>();
        (await controller.Complete(id, completion, cancellation.Token)).Should().BeOfType<NoContentResult>();
        (await controller.Fail(id, failure, cancellation.Token)).Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Complete_LostLease_ReturnsConflictForLateWorker()
    {
        var service = new Mock<IMediaJobService>();
        service.Setup(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<JobCompletion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Conflict("LeaseLost", "Expired or cancelled.")));
        var result = (await new InternalMediaJobsController(service.Object).Complete(Guid.NewGuid(), new(Guid.NewGuid(), "private/video", "1", null, null, null), default))
            .Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(409);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("LeaseLost");
    }

    [TestMethod]
    public void Controller_RequiresWorkerFilterEvenWithoutSellerCookie()
    {
        var type = typeof(InternalMediaJobsController);
        type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().ContainSingle();
        type.GetCustomAttributes(typeof(TypeFilterAttribute), true).Cast<TypeFilterAttribute>().Single().ImplementationType.Should().Be(typeof(MediaWorkerAuthorizationFilter));
    }
}

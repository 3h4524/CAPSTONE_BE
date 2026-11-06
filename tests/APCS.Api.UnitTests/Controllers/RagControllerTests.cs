using APCS.Api.Controllers;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class RagControllerTests
{
    [TestMethod]
    public async Task Ingest_VideoExperience_ReturnsBadRequestWithoutCallingGenericRagService()
    {
        var service = new Mock<IRagService>(MockBehavior.Strict);
        var request = new RagIngestRequest("Lesson", "video-experience", null, "1", "en", "Content",
            ContentType: RagContentTypes.Video);

        var action = await new RagController(service.Object).Ingest(request, CancellationToken.None);

        action.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Search_VideoExperienceScope_ReturnsBadRequestWithoutCallingGenericRagService()
    {
        var service = new Mock<IRagService>(MockBehavior.Strict);
        var request = new RagSearchRequest("hands", ContentType: RagContentTypes.Video,
            VideoExperience: new(Guid.NewGuid(), "lifestyle_in_use", "minimax_h3", "touch collar"));

        var action = await new RagController(service.Object).Search(request, CancellationToken.None);

        action.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AdminDiagnosticsAndEvaluation_ForwardRequestsAndCancellation()
    {
        var search = new RagSearchRequest("listing title", 3);
        var diagnostics = new RagSearchDiagnosticsDto("listing title", 3, 30, 4, 2, 3, 4, 12, []);
        var evaluation = new RagEvaluationRequest([new RagEvaluationCaseRequest("case-1", search)]);
        var evaluationResponse = new RagEvaluationResponse("run-1", DateTimeOffset.UtcNow, 1, 100, 100, 100, 100, 4, []);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IRagService>(MockBehavior.Strict);
        service.Setup(x => x.SearchWithDiagnosticsAsync(search, cancellation.Token)).ReturnsAsync(diagnostics);
        service.Setup(x => x.EvaluateAsync(evaluation, cancellation.Token)).ReturnsAsync(evaluationResponse);
        var controller = new RagController(service.Object);

        (await controller.SearchWithDiagnostics(search, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(diagnostics);
        (await controller.Evaluate(evaluation, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(evaluationResponse);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task AdminHealthAndReingest_ForwardRequestsAndCancellation()
    {
        var health = new RagCorpusHealthResponse(
            DateTimeOffset.UtcNow,
            "listing",
            30,
            1,
            1,
            0,
            0,
            0,
            0,
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            []);
        var reingest = new RagReingestRequest(ContentType: "listing", StaleOnly: true, StaleDays: 30);
        var reingestResponse = new RagReingestResponse(1, 1, 0, [Guid.NewGuid()]);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IRagService>(MockBehavior.Strict);
        service.Setup(x => x.GetCorpusHealthAsync("listing", 30, cancellation.Token)).ReturnsAsync(health);
        service.Setup(x => x.ReingestAsync(reingest, cancellation.Token)).ReturnsAsync(reingestResponse);
        var controller = new RagController(service.Object);

        (await controller.GetCorpusHealth("listing", 30, cancellation.Token)).Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(health);
        (await controller.Reingest(reingest, cancellation.Token)).Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(reingestResponse);
        service.VerifyAll();
    }
}

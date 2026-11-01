using APCS.Api.Controllers;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class RagControllerTests
{
    [TestMethod]
    public async Task IngestAndSearch_ForwardRequestsAndCancellation()
    {
        var documentId = Guid.NewGuid();
        var ingest = new RagIngestRequest("Policy", "etsy", null, "1", "en", "Content");
        var search = new RagSearchRequest("listing title", 3);
        IReadOnlyList<RagSearchResultDto> results = [new(Guid.NewGuid(), documentId, "Policy", "listing", "Content", null, .9, new Dictionary<string, object?>())];
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IRagService>(MockBehavior.Strict);
        service.Setup(x => x.IngestAsync(ingest, cancellation.Token)).ReturnsAsync(documentId);
        service.Setup(x => x.SearchAsync(search, cancellation.Token)).ReturnsAsync(results);
        var controller = new RagController(service.Object);

        (await controller.Ingest(ingest, cancellation.Token)).Should().BeOfType<AcceptedResult>().Which.Value.Should().NotBeNull();
        (await controller.Search(search, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(results);
        service.VerifyAll();
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

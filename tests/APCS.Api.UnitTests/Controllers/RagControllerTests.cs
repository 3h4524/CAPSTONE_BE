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

        (await controller.Ingest(ingest, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().NotBeNull();
        (await controller.Search(search, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(results);
        service.VerifyAll();
    }
}

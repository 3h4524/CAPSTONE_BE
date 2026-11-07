using APCS.Api.Controllers;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class RagListingEvaluationControllerTests
{
    [TestMethod]
    public async Task EvaluateRetrieval_ForwardsProductAndCancellation()
    {
        var productId = Guid.NewGuid();
        var request = new ListingRetrievalEvaluationRequest(productId);
        var emptyDiagnostics = new RagSearchDiagnosticsDto("product query", 5, 20, 0, 0, 0, 0, 2, []);
        var response = new ListingRetrievalEvaluationResponse(
            productId,
            "Botanical mug",
            "mug",
            "plant lovers",
            "gift buyers",
            "A ceramic mug.",
            ["botanical", "mug"],
            null,
            "Botanical mug; mug; plant lovers; gift buyers; botanical, mug; A ceramic mug.; ",
            4,
            emptyDiagnostics,
            emptyDiagnostics);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IListingContentService>(MockBehavior.Strict);
        service.Setup(x => x.EvaluateRetrievalForAdminAsync(productId, cancellation.Token))
            .ReturnsAsync(Result.Success(response));
        var controller = new RagListingEvaluationController(service.Object);

        (await controller.EvaluateRetrieval(request, cancellation.Token))
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }
}

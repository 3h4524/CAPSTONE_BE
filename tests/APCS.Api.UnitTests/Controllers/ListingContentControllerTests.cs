using APCS.Api.Controllers;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class ListingContentControllerTests
{
    [TestMethod]
    public async Task Generate_GetAndEvaluate_ForwardProductAndCancellation()
    {
        var productId = Guid.NewGuid();
        var feedback = new ListingSeoFeedbackDto(60, 50, 55, 70, 65, 45, 4, ["Improve tags."], "regenerate", true);
        var listing = new ListingContentResponseDto(productId, Guid.NewGuid(), "Title", "Description", [], 2, "gemini", DateTime.UtcNow);
        var seo = new ListingSeoScoreResponseDto(productId, listing.ListingContentId, 60, 50, 55, 70, 65, 45, 4, ["Improve tags."], "regenerate", true, "v1", DateTime.UtcNow);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IListingContentService>(MockBehavior.Strict);
        service.Setup(x => x.GenerateAsync(productId, cancellation.Token, feedback)).ReturnsAsync(Result.Success(listing));
        service.Setup(x => x.GetAsync(productId, cancellation.Token)).ReturnsAsync(Result.Success(listing));
        service.Setup(x => x.EvaluateSeoAsync(productId, cancellation.Token)).ReturnsAsync(Result.Success(seo));
        var controller = new ListingContentController(service.Object);

        (await controller.Generate(productId, feedback, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(listing);
        (await controller.Get(productId, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(listing);
        (await controller.EvaluateSeo(productId, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(seo);
        service.VerifyAll();
    }
}

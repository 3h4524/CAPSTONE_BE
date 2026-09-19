using APCS.Api.Controllers;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class BatchMockupsControllerTests
{
    [TestMethod]
    public async Task List_FiltersByProductType()
    {
        IReadOnlyList<MockupTemplateResponseDto> response =
        [
            new(Guid.NewGuid(), "Tee A", "tshirt", "https://x/a.jpg", null, "{}", 2000, 2000, 0)
        ];
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.ListAsync("tshirt", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new MockupTemplatesController(service.Object).List("tshirt", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Apply_ValidIds_ReturnsSelection()
    {
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var response = new BatchMockupSelectionResponseDto(batchId, [templateId]);
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.ApplyAsync(batchId, It.IsAny<ApplyMockupTemplatesRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(response));

        var result = await new BatchMockupsController(service.Object)
            .Apply(batchId, new ApplyMockupTemplatesRequestDto([templateId]), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Apply_OtherSellersBatch_ReturnsNotFound()
    {
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.ApplyAsync(It.IsAny<Guid>(), It.IsAny<ApplyMockupTemplatesRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BatchMockupSelectionResponseDto>(Error.NotFound("BatchMockups.BatchNotFound", "Missing.")));

        var result = await new BatchMockupsController(service.Object)
            .Apply(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([Guid.NewGuid()]), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }
}

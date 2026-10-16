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
            new(Guid.NewGuid(), "Tee A", "tshirt", "https://x/a.jpg", null, "{}", 2000, 2000, 0, true, false, false, false, null, null)
        ];
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.ListAsync("tshirt", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new MockupTemplatesController(service.Object).List("tshirt", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Get_Found_ReturnsSelection()
    {
        var batchId = Guid.NewGuid();
        var response = new BatchMockupSelectionResponseDto(batchId, [Guid.NewGuid()], []);
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.GetSelectionAsync(batchId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new BatchMockupsController(service.Object).Get(batchId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Get_MissingBatch_ReturnsNotFound()
    {
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.GetSelectionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BatchMockupSelectionResponseDto>(Error.NotFound("BatchMockups.BatchNotFound", "Missing.")));

        var result = await new BatchMockupsController(service.Object).Get(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Apply_ValidIds_ReturnsSelection()
    {
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var response = new BatchMockupSelectionResponseDto(batchId, [templateId], []);
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

    [TestMethod]
    public async Task SaveOverride_ReturnsTheJobsMockups()
    {
        var batchId = Guid.NewGuid();
        var request = new SaveMockupOverrideRequestDto("slot", new MockupOverrideDto(Guid.NewGuid(), null, null, null), Guid.NewGuid());
        var response = new GenerateAllMockupsResultDto(1, 0, 0, [], []);
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.SaveOverrideAsync(batchId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new BatchMockupsController(service.Object).SaveOverride(batchId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task ResetOverride_PassesTheSlotAndTheReplacedMockup()
    {
        var batchId = Guid.NewGuid();
        var replaced = Guid.NewGuid();
        var service = new Mock<IMockupTemplateService>();
        service.Setup(x => x.ResetOverrideAsync(batchId, "slot", replaced, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<GenerateAllMockupsResultDto>(Error.Conflict("BatchMockups.JobGenerating", "Busy.")));

        var result = await new BatchMockupsController(service.Object).ResetOverride(batchId, "slot", replaced, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
    }
}

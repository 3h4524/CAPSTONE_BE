using APCS.Api.Controllers;
using APCS.Application.Features.BatchProductPrompts;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using APCS.Application.Features.BatchProductPrompts.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class BatchProductPromptsControllerTests
{
    [TestMethod]
    public async Task Get_ReturnsEffectivePrompt()
    {
        var rowId = Guid.NewGuid();
        var response = new BatchProductPromptResponseDto(rowId, "Cat", "Water", "Calm", "", "", "default", "base", "niche", "mod", "effective", 9, false, true);
        var service = new Mock<IBatchProductPromptService>();
        service.Setup(x => x.GetEffectiveAsync(rowId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new BatchProductPromptsController(service.Object).Get(rowId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Save_TooLong_ReturnsBadRequest()
    {
        var service = new Mock<IBatchProductPromptService>();
        service.Setup(x => x.SaveAsync(It.IsAny<Guid>(), It.IsAny<UpdateBatchProductPromptRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BatchProductPromptResponseDto>(Error.Validation("Too long.")));

        var result = await new BatchProductPromptsController(service.Object)
            .Save(Guid.NewGuid(), new UpdateBatchProductPromptRequestDto("a", "b", "c", "", ""), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [TestMethod]
    public async Task Restore_Cleared_ReturnsDefault()
    {
        var rowId = Guid.NewGuid();
        var response = new BatchProductPromptResponseDto(rowId, "Cat", "Water", "Calm", "", "", "default", "base", "niche", "mod", "default", 7, false, true);
        var service = new Mock<IBatchProductPromptService>();
        service.Setup(x => x.RestoreAsync(rowId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new BatchProductPromptsController(service.Object).Restore(rowId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }
}

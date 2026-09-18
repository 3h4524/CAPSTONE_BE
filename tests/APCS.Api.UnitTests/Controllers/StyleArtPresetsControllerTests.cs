using APCS.Api.Controllers;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class StyleArtPresetsControllerTests
{
    [TestMethod]
    public async Task List_Success_ReturnsPresetsAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<StyleArtPresetResponseDto> response =
        [
            new(Guid.NewGuid(), "Vintage", "Retro", "vintage", null, ["Apparel"])
        ];
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.ListActiveAsync(cancellation.Token)).ReturnsAsync(Result.Success(response));

        var result = await new StyleArtPresetsController(service.Object).List(cancellation.Token);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.Verify(x => x.ListActiveAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task List_Unauthenticated_ReturnsProblem()
    {
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<StyleArtPresetResponseDto>>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in.")));

        var result = await new StyleArtPresetsController(service.Object).List(CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(401);
    }
}

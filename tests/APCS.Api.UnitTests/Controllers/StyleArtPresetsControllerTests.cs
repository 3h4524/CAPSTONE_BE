using APCS.Api.Contracts.StyleArtPresets;
using APCS.Api.Controllers;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
            new(Guid.NewGuid(), "Vintage", "Retro", "vintage", null, ["Apparel"], true, false)
        ];
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.ListMineAsync(cancellation.Token)).ReturnsAsync(Result.Success(response));

        var result = await new StyleArtPresetsController(service.Object).List(cancellation.Token);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.Verify(x => x.ListMineAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task List_Unauthenticated_ReturnsProblem()
    {
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.ListMineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<StyleArtPresetResponseDto>>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in.")));

        var result = await new StyleArtPresetsController(service.Object).List(CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(401);
    }

    [TestMethod]
    public async Task Create_Success_ReturnsCreatedWithLocation()
    {
        var form = new CreateStyleArtPresetForm { Name = "Neon", Description = "Glow", StyleModifiers = "neon" };
        var created = new StyleArtPresetResponseDto(Guid.NewGuid(), "Neon", "Glow", "neon", null, [], false, true);
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.CreateAsync(It.IsAny<CreateStyleArtPresetRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(created));

        var result = await new StyleArtPresetsController(service.Object).Create(form, CancellationToken.None);

        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.Value.Should().BeSameAs(created);
    }

    [TestMethod]
    public async Task Get_Found_ReturnsPreset()
    {
        var id = Guid.NewGuid();
        var response = new StyleArtPresetResponseDto(id, "Vintage", "Retro", "vintage", null, [], true, false);
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.GetMineAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(response));

        var result = await new StyleArtPresetsController(service.Object).Get(id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Get_Missing_ReturnsNotFoundProblem()
    {
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.GetMineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<StyleArtPresetResponseDto>(Error.NotFound("StyleArtPresets.NotFound", "Missing.")));

        var result = await new StyleArtPresetsController(service.Object).Get(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Update_Success_ReturnsPreset()
    {
        var id = Guid.NewGuid();
        var form = new UpdateStyleArtPresetForm { Name = "Mine+", Description = "New description", StyleModifiers = "new" };
        var response = new StyleArtPresetResponseDto(id, "Mine+", "New description", "new", null, [], false, true);
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.UpdateAsync(id, It.IsAny<UpdateStyleArtPresetRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(response));

        var result = await new StyleArtPresetsController(service.Object).Update(id, form, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
    }

    [TestMethod]
    public async Task Delete_OwnPreset_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var result = await new StyleArtPresetsController(service.Object).Delete(id, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task Delete_SystemPreset_ReturnsForbiddenProblem()
    {
        var id = Guid.NewGuid();
        var service = new Mock<IStyleArtPresetService>();
        service.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Forbidden("StyleArtPresets.NotOwner", "System art styles cannot be deleted.")));

        var result = await new StyleArtPresetsController(service.Object).Delete(id, CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(403);
    }
}

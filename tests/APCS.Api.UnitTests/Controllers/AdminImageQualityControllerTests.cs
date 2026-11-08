using System.Reflection;
using System.Security.Claims;
using APCS.Api.Controllers;
using APCS.Application.Features.ImageQuality;
using APCS.Application.Features.ImageQuality.Dtos.Request;
using APCS.Application.Features.ImageQuality.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminImageQualityControllerTests
{
    private readonly Mock<IImageQualityService> _service = new();
    private readonly AdminImageQualityController _controller;

    public AdminImageQualityControllerTests()
    {
        _controller = new AdminImageQualityController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "admin@apcs.test")], "test")),
                },
            },
        };
    }

    private static ImageEvaluationSummaryResponseDto Summary(Guid id) =>
        new(id, "Run", null, DateTimeOffset.UnixEpoch, "admin@apcs.test", 1, 1, []);

    [TestMethod]
    public void Controller_IsForAdminsOnly()
    {
        var authorize = typeof(AdminImageQualityController).GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin,Super Admin");
    }

    [TestMethod]
    public void ImportEvaluation_LimitsTheRequestSize()
    {
        var limit = typeof(AdminImageQualityController)
            .GetMethod(nameof(AdminImageQualityController.ImportEvaluation))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();

        limit.Should().NotBeNull();
    }

    [TestMethod]
    public async Task ImportEvaluation_Stored_ReturnsCreatedWithTheUploader()
    {
        var id = Guid.NewGuid();
        var request = new ImportImageEvaluationRequestDto("Run", null, [], []);
        _service.Setup(s => s.ImportEvaluationAsync(request, "admin@apcs.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Summary(id)));

        var result = await _controller.ImportEvaluation(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(AdminImageQualityController.GetEvaluation));
        created.RouteValues!["id"].Should().Be(id);
    }

    [TestMethod]
    public async Task ImportEvaluation_InvalidFile_ReturnsBadRequest()
    {
        var request = new ImportImageEvaluationRequestDto("Run", null, [], []);
        _service.Setup(s => s.ImportEvaluationAsync(request, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ImageEvaluationSummaryResponseDto>(Error.Validation("Bad file")));

        var result = await _controller.ImportEvaluation(request, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task GetEvaluation_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.GetEvaluationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ImageEvaluationDetailResponseDto>(Error.NotFound("image_quality.evaluation_not_found", "Gone")));

        var result = await _controller.GetEvaluation(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task DeleteEvaluation_Removed_ReturnsNoContent()
    {
        _service.Setup(s => s.DeleteEvaluationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.DeleteEvaluation(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task GetLiveStats_ReturnsOk()
    {
        var stats = new ImageQualityLiveStatsResponseDto(DateTimeOffset.UnixEpoch, []);
        _service.Setup(s => s.GetLiveStatsAsync(It.IsAny<GetImageQualityLiveStatsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(stats));

        var result = await _controller.GetLiveStats(new GetImageQualityLiveStatsRequestDto(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(stats);
    }
}

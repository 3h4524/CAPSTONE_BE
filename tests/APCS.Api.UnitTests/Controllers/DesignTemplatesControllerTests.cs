using System.Reflection;
using APCS.Api.Controllers;
using APCS.Application.Features.DesignTemplates;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class DesignTemplatesControllerTests
{
    [TestMethod]
    public void Controller_RequiresSellerRoleAndDisablesResponseCaching()
    {
        var controllerType = typeof(DesignTemplatesController);

        var authorization = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var responseCache = controllerType.GetCustomAttribute<ResponseCacheAttribute>();

        authorization.Should().NotBeNull();
        authorization!.Roles.Should().Be(AuthConstants.UserRole);
        responseCache.Should().NotBeNull();
        responseCache!.NoStore.Should().BeTrue();
        responseCache.Location.Should().Be(ResponseCacheLocation.None);
    }

    [TestMethod]
    public async Task Create_OnSuccess_ReturnsCreatedAtGetWithNewId()
    {
        using var cancellation = new CancellationTokenSource();
        var request = CreateRequest();
        var response = Detail(false);
        var service = new Mock<IDesignTemplateService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.CreateAsync(request, cancellation.Token))
            .ReturnsAsync(Result.Success(response));

        var action = await new DesignTemplatesController(service.Object)
            .Create(request, cancellation.Token);

        var created = action.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(DesignTemplatesController.Get));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(response.Id);
        created.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Clone_OnSuccess_ReturnsCreatedAtGetForPersonalCopy()
    {
        using var cancellation = new CancellationTokenSource();
        var sourceId = Guid.NewGuid();
        var response = Detail(false);
        var service = new Mock<IDesignTemplateService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.CloneAsync(sourceId, cancellation.Token))
            .ReturnsAsync(Result.Success(response));

        var action = await new DesignTemplatesController(service.Object)
            .Clone(sourceId, cancellation.Token);

        var created = action.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(DesignTemplatesController.Get));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(response.Id);
        created.RouteValues!["id"].Should().NotBe(sourceId);
        response.CanEdit.Should().BeTrue();
        response.CanClone.Should().BeFalse();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Update_WhenSystemTemplateIsTargeted_ReturnsForbiddenProblem()
    {
        var id = Guid.NewGuid();
        var request = UpdateRequest();
        var service = new Mock<IDesignTemplateService>();
        service.Setup(candidate => candidate.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<DesignTemplateDetailResponseDto>(
                Error.Forbidden("design_templates.system_read_only", "System templates are read-only.")));

        var action = await new DesignTemplatesController(service.Object)
            .Update(id, request, CancellationToken.None);

        var forbidden = action.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(403);
        forbidden.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be("design_templates.system_read_only");
    }

    [TestMethod]
    public async Task Delete_OnSuccess_ReturnsNoContentAndForwardsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        var service = new Mock<IDesignTemplateService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.DeleteAsync(id, cancellation.Token))
            .ReturnsAsync(Result.Success());

        var action = await new DesignTemplatesController(service.Object)
            .Delete(id, cancellation.Token);

        action.Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    private static CreateDesignTemplateRequestDto CreateRequest() =>
        new(
            "Personal template",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject}",
            null,
            []);

    private static UpdateDesignTemplateRequestDto UpdateRequest() =>
        new(
            "Updated template",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject}",
            null,
            []);

    private static DesignTemplateDetailResponseDto Detail(bool system) =>
        new(
            Guid.NewGuid(),
            system ? "System" : "Personal",
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject}",
            null,
            [],
            "Description",
            null,
            system,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            !system,
            !system,
            system);
}

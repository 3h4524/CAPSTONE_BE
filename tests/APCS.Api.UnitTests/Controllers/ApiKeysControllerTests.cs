using APCS.Api.Controllers;
using APCS.Application.Features.ApiKeys;
using APCS.Application.Features.ApiKeys.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class ApiKeysControllerTests
{
    [TestMethod]
    public async Task Mutations_ForwardIdsRequestsAndCancellation_ReturnNoContent()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new APCS.Application.Features.ApiKeys.Dtos.Request.SaveApiKeyRequestDto();
        var id = Guid.NewGuid();
        var service = new Mock<IApiKeyService>(MockBehavior.Strict);
        service.Setup(x => x.SaveAsync(null, request, cancellation.Token)).ReturnsAsync(Result.Success());
        service.Setup(x => x.SaveAsync(id, request, cancellation.Token)).ReturnsAsync(Result.Success());
        service.Setup(x => x.DeleteAsync(id, cancellation.Token)).ReturnsAsync(Result.Success());
        service.Setup(x => x.ValidateAsync(id, cancellation.Token)).ReturnsAsync(Result.Success());
        var controller = new ApiKeysController(service.Object);
        (await controller.Add(request, cancellation.Token)).Should().BeOfType<NoContentResult>();
        (await controller.Edit(id, request, cancellation.Token)).Should().BeOfType<NoContentResult>();
        (await controller.Delete(id, cancellation.Token)).Should().BeOfType<NoContentResult>();
        (await controller.Validate(id, cancellation.Token)).Should().BeOfType<NoContentResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Add_Duplicate_ReturnsConflictProblem()
    {
        var request = new APCS.Application.Features.ApiKeys.Dtos.Request.SaveApiKeyRequestDto();
        var service = new Mock<IApiKeyService>();
        service.Setup(x => x.SaveAsync(null, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Conflict("MSG51", "Already connected.")));
        var result = (await new ApiKeysController(service.Object).Add(request, CancellationToken.None))
            .Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(409);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("MSG51");
    }

    [TestMethod]
    public async Task List_Success_ReturnsOverviewAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var response = new ListApiKeysResponseDto([], 0, 0, false, null);
        var service = new Mock<IApiKeyService>();
        service.Setup(x => x.ListMineAsync(cancellation.Token)).ReturnsAsync(Result.Success(response));
        var result = await new ApiKeysController(service.Object).List(cancellation.Token);
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.Verify(x => x.ListMineAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task List_DatabaseFailure_ReturnsMsg16Problem()
    {
        var service = new Mock<IApiKeyService>();
        service.Setup(x => x.ListMineAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ListApiKeysResponseDto>(Error.Failure("MSG16", "Please retry.")));
        var result = await new ApiKeysController(service.Object).List(CancellationToken.None);
        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("MSG16");
    }

    [TestMethod]
    public void Controller_RequiresSellerRoleAndDisablesResponseCaching()
    {
        var type = typeof(ApiKeysController);
        type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single()
            .Roles.Should().Be("Seller");
        type.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>().Single()
            .NoStore.Should().BeTrue();
    }
}

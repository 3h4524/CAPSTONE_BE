using APCS.Api.Extensions;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.UnitTests.Extensions;

[TestClass]
public sealed class ResultExtensionsTests
{
    [TestMethod]
    public void ToActionResult_WithGenericSuccess_ReturnsOkWithValue()
    {
        var action = Result.Success("value").ToActionResult(new TestController());

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be("value");
    }

    [TestMethod]
    public void ToActionResult_WithNonGenericSuccess_ReturnsNoContent()
    {
        Result.Success().ToActionResult(new TestController()).Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    [DataRow(ErrorType.Validation, 400)]
    [DataRow(ErrorType.Unauthorized, 401)]
    [DataRow(ErrorType.Forbidden, 403)]
    [DataRow(ErrorType.NotFound, 404)]
    [DataRow(ErrorType.Conflict, 409)]
    [DataRow(ErrorType.Failure, 500)]
    [DataRow(ErrorType.None, 500)]
    public void ToActionResult_WithFailure_MapsProblemDetailsStatus(ErrorType errorType, int statusCode)
    {
        var details = new Dictionary<string, string[]> { ["field"] = ["invalid"] };
        var error = new Error("sample.error", "Safe message", errorType, details);

        var action = Result.Failure<string>(error).ToActionResult(new TestController());

        var objectResult = action.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(statusCode);
        var problem = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(statusCode);
        problem.Title.Should().Be("Safe message");
        problem.Type.Should().Be("https://apcs/errors/sample.error");
        problem.Extensions["code"].Should().Be("sample.error");
        problem.Extensions["errors"].Should().BeSameAs(details);
    }

    [TestMethod]
    public void ToActionResult_WithNullArguments_Throws()
    {
        Result<string> result = null!;
        var controller = new TestController();

        var nullResult = () => result.ToActionResult(controller);
        var nullController = () => Result.Success("value").ToActionResult(null!);

        nullResult.Should().Throw<ArgumentNullException>();
        nullController.Should().Throw<ArgumentNullException>();
    }

    private sealed class TestController : ControllerBase;
}

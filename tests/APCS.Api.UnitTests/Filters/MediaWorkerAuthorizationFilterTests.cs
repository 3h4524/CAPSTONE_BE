using APCS.Api.Filters;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace APCS.Api.UnitTests.Filters;

[TestClass]
public sealed class MediaWorkerAuthorizationFilterTests
{
    [TestMethod]
    [DataRow("unconfigured")]
    [DataRow("short_configuration")]
    [DataRow("whitespace_configuration")]
    [DataRow("missing_header")]
    [DataRow("short_header")]
    [DataRow("mismatch")]
    [DataRow("oversized_header")]
    public void OnAuthorization_InvalidKey_ReturnsUnauthorizedWithoutDisclosingCredential(string condition)
    {
        var expected = condition switch { "unconfigured" => null, "short_configuration" => new string('a', 31), "whitespace_configuration" => new string(' ', 32), _ => new string('a', 32) };
        var provided = condition switch { "missing_header" => null, "short_header" => "short", "mismatch" => new string('b', 32), "oversized_header" => new string('a', 257), _ => expected };
        var context = Context(provided);
        Filter(expected).OnAuthorization(context);
        var denied = context.Result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        denied.StatusCode.Should().Be(401);
        var problem = denied.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(401);
        problem.Title.Should().Be("Worker authentication required.");
        problem.Detail.Should().BeNull();
        problem.Extensions.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(256)]
    public void OnAuthorization_ExactValidKeyAtBoundary_AllowsControllerExecution(int length)
    {
        var key = new string('a', length);
        var context = Context(key);
        Filter(key).OnAuthorization(context);
        context.Result.Should().BeNull();
    }

    [TestMethod]
    public void OnAuthorization_MultipleHeaderValues_DoesNotAcceptOneValidValue()
    {
        var key = new string('a', 32);
        var context = Context(null);
        context.HttpContext.Request.Headers["X-Media-Worker-Key"] = new Microsoft.Extensions.Primitives.StringValues([key, "other"]);
        Filter(key).OnAuthorization(context);
        context.Result.Should().BeOfType<UnauthorizedObjectResult>().Which.StatusCode.Should().Be(401);
    }

    private static MediaWorkerAuthorizationFilter Filter(string? expected) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["MediaWorker:ApiKey"] = expected }).Build());
    private static AuthorizationFilterContext Context(string? provided)
    {
        var http = new DefaultHttpContext();
        if (provided != null) http.Request.Headers["X-Media-Worker-Key"] = provided;
        return new(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }
}

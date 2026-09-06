using System.Text.Json;
using APCS.Api.Middleware;
using APCS.Common.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace APCS.Api.UnitTests.Middleware;

[TestClass]
public sealed class GlobalExceptionMiddlewareTests
{
    [TestMethod]
    public async Task InvokeAsync_WhenNextSucceeds_PassesThrough()
    {
        var called = false;
        var middleware = new GlobalExceptionMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            new TestLogger<GlobalExceptionMiddleware>());

        await middleware.InvokeAsync(CreateContext());

        called.Should().BeTrue();
    }

    [TestMethod]
    public async Task InvokeAsync_WhenUnexpectedExceptionOccurs_WritesSafeProblemDetails()
    {
        var logger = new TestLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("sensitive database detail"),
            logger);
        var context = CreateContext();
        context.TraceIdentifier = "trace-123";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Be("application/problem+json");
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        root.GetProperty("code").GetString().Should().Be(ErrorCodes.Unexpected);
        root.GetProperty("traceId").GetString().Should().Be("trace-123");
        root.GetRawText().Should().NotContain("sensitive database detail");
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
    }

    [TestMethod]
    public async Task InvokeAsync_WhenClientCancels_LogsAndDoesNotWriteError()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var logger = new TestLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            _ => Task.FromCanceled(source.Token),
            logger);
        var context = CreateContext();
        context.RequestAborted = source.Token;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information);
    }

    [TestMethod]
    public async Task InvokeAsync_WhenResponseAlreadyStarted_RethrowsOriginalException()
    {
        var expected = new InvalidOperationException("boom");
        var middleware = new GlobalExceptionMiddleware(
            _ => throw expected,
            new TestLogger<GlobalExceptionMiddleware>());
        var responseFeature = new StartedResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        var context = new DefaultHttpContext(features);

        var act = () => middleware.InvokeAsync(context);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    private static DefaultHttpContext CreateContext() => new()
    {
        Response = { Body = new MemoryStream() }
    };

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}

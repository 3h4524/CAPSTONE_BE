using System.Net;
using System.Net.Http;
using System.Text;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ProcessVideoQualityEvidenceRunnerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "apcs-video-quality-runner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenEvaluationIsDisabled_ReturnsSkippedWithoutCallingTheMediaClient()
    {
        var runner = CreateRunner(new VideoQualityOptions { Enabled = false }, new ThrowingHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("skipped");
        result.EvaluatorVersion.Should().Be("video-quality-v1");
        result.Metrics.Should().BeEmpty();
        result.Detail.Should().Be("Local PaddleOCR/VBench evaluation is disabled.");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenNoScriptsAreConfigured_ReturnsSkippedWithPerToolReasons()
    {
        var runner = CreateRunner(new VideoQualityOptions { Enabled = true }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("skipped");
        result.Detail.Should().Be("No local PaddleOCR or VBench script is configured.");
        ToolStatus(result, "paddleOcr").Should().Be("skipped");
        ToolStatus(result, "vbench").Should().Be("skipped");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenConfiguredAdapterCompletes_ReturnsCompletedAndKeepsAdapterMetrics()
    {
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PythonExecutable = PowerShellExecutable(),
            PaddleOcrScriptPath = WriteAdapter("{\"status\":\"completed\",\"score\":0.8}"),
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("completed");
        result.Detail.Should().BeNull();
        ToolStatus(result, "paddleOcr").Should().Be("completed");
        ToolStatus(result, "vbench").Should().Be("skipped");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenConfiguredAdapterIsUncertain_ReturnsUncertain()
    {
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PythonExecutable = PowerShellExecutable(),
            PaddleOcrScriptPath = WriteAdapter("{\"status\":\"uncertain\"}"),
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("uncertain");
        ToolStatus(result, "paddleOcr").Should().Be("uncertain");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenConfiguredAdapterReportsFailure_ReturnsFailedWithAdapterDetail()
    {
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PythonExecutable = PowerShellExecutable(),
            PaddleOcrScriptPath = WriteAdapter("{\"status\":\"failed\"}"),
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("failed");
        result.Detail.Should().Contain("paddleOcr: the adapter reported failure.");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenConfiguredScriptIsMissing_ReturnsFailedWithConfigurationError()
    {
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PaddleOcrScriptPath = Path.Combine(_root, "missing-adapter.py"),
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("failed");
        ToolStatus(result, "paddleOcr").Should().Be("failed");
        result.Detail.Should().Contain("Configured script was not found");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenAdapterWritesInvalidJson_ReturnsFailedWithParserError()
    {
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PythonExecutable = PowerShellExecutable(),
            PaddleOcrScriptPath = WriteAdapter("not-json"),
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("failed");
        result.Detail.Should().Contain("Quality tool output is not valid JSON.");
    }

    [TestMethod]
    public async Task EvaluateAsync_WhenAdapterExitsNonZero_ReturnsFailedWithExitCode()
    {
        var script = Path.Combine(_root, "fails.ps1");
        File.WriteAllText(script, "exit 7", Encoding.UTF8);
        var runner = CreateRunner(new VideoQualityOptions
        {
            Enabled = true,
            PythonExecutable = PowerShellExecutable(),
            PaddleOcrScriptPath = script,
        }, new BytesHandler());

        var result = await runner.EvaluateAsync(Request());

        result.Status.Should().Be("failed");
        result.Detail.Should().Contain("Quality tool exited with code");
    }

    private ProcessVideoQualityEvidenceRunner CreateRunner(VideoQualityOptions options, HttpMessageHandler handler) =>
        new(MsOptions.Create(options), new StaticClientFactory(new HttpClient(handler)));

    private VideoQualityEvidenceRequest Request() =>
        new("https://media.test/scene.mp4", [1, 2, 3, 4], 0);

    private string WriteAdapter(string output)
    {
        var script = Path.Combine(_root, $"adapter-{Guid.NewGuid():N}.ps1");
        var escapedOutput = output.Replace("'", "''");
        File.WriteAllText(script, $$"""
            param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ToolArguments)
            $outputIndex = [Array]::IndexOf($ToolArguments, "--output")
            [System.IO.File]::WriteAllText($ToolArguments[$outputIndex + 1], '{{escapedOutput}}')
            """, Encoding.UTF8);
        return script;
    }

    private static string PowerShellExecutable() =>
        OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

    private static string ToolStatus(VideoQualityEvidenceResult result, string name)
    {
        var metric = result.Metrics[name].Should().BeOfType<Dictionary<string, object?>>().Subject;
        return metric["status"]?.ToString() ?? string.Empty;
    }

    private sealed class StaticClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4]),
        });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new AssertFailedException("HTTP should not be called.");
    }
}

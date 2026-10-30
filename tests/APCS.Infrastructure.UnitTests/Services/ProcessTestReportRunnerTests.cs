using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ProcessTestReportRunnerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "apcs-test-report-runner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "frontend"));
        File.WriteAllText(Path.Combine(_root, "report-source.json"), "{\"stats\":{}}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ProcessTestReportRunner CreateRunner(string command)
    {
        var options = MsOptions.Create(new TestReportOptions
        {
            WorkingDirectory = _root,
            FrontendRoot = "frontend",
            ReportFileName = "report.json",
            RunnerCommand = command,
            TimeoutMinutes = 2,
        });

        return new ProcessTestReportRunner(options);
    }

    [TestMethod]
    public async Task RunAsync_BringsBackTheReportTheCommandWrote()
    {
        var runner = CreateRunner(ShellCommand(
            "copy /y report-source.json frontend\\report.json > nul",
            "cp report-source.json frontend/report.json"));

        var result = await runner.RunAsync("run-1", CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.RawReport.Should().Be("{\"stats\":{}}");
    }

    [TestMethod]
    public async Task RunAsync_BringsBackNothingWhenTheCommandWroteNoReport()
    {
        var runner = CreateRunner(NoOpCommand());

        var result = await runner.RunAsync("run-1", CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.RawReport.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_KeepsWhatTheCommandPrinted()
    {
        var runner = CreateRunner("echo 'the suite ran'");

        var result = await runner.RunAsync("run-1", CancellationToken.None);

        result.Log.Should().Contain("the suite ran");
    }

    [TestMethod]
    public async Task RunAsync_DoesNotTakeTheReportOfAnEarlierRun()
    {
        var runner = CreateRunner(NoOpCommand());
        await File.WriteAllTextAsync(Path.Combine(_root, "frontend", "report.json"), "{\"stats\":{}}");

        var result = await runner.RunAsync("run-1", CancellationToken.None);

        result.RawReport.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_ReportsTheExitCodeOfAFailedCommand()
    {
        var runner = CreateRunner(ShellCommand("exit /b 3", "exit 3"));

        var result = await runner.RunAsync("run-1", CancellationToken.None);

        result.ExitCode.Should().Be(3);
    }

    private static string NoOpCommand() => ShellCommand("ver > nul", "true");

    private static string ShellCommand(string windows, string unix) =>
        OperatingSystem.IsWindows() ? windows : unix;
}

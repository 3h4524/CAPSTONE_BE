using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class FileTestReportStoreTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "apcs-test-reports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private FileTestReportStore CreateStore()
    {
        var options = MsOptions.Create(new TestReportOptions { Directory = _directory });
        return new FileTestReportStore(options);
    }

    private static TestReportRunDto Run(string runId, DateTime startedAtUtc) => new()
    {
        RunId = runId,
        StartedAtUtc = startedAtUtc,
        Status = "passed",
        PassedTests = 3,
    };

    [TestMethod]
    public async Task SaveRunAsync_KeepsTheRunAndItsReport()
    {
        var store = CreateStore();

        await store.SaveRunAsync(Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)), "{\"stats\":{}}", CancellationToken.None);

        var run = await store.GetRunAsync("run-1", CancellationToken.None);
        var report = await store.GetRawReportAsync("run-1", CancellationToken.None);

        run.Should().NotBeNull();
        run!.PassedTests.Should().Be(3);
        report.Should().Be("{\"stats\":{}}");
    }

    [TestMethod]
    public async Task ListAsync_ReturnsTheRunsNewestFirst()
    {
        var store = CreateStore();
        await store.SaveRunAsync(Run("run-old", new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc)), null, CancellationToken.None);
        await store.SaveRunAsync(Run("run-new", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)), null, CancellationToken.None);

        var runs = await store.ListAsync(CancellationToken.None);

        runs.Select(item => item.RunId).Should().Equal("run-new", "run-old");
    }

    [TestMethod]
    public async Task GetRunAsync_ReturnsNothingForAnUnknownRun()
    {
        var store = CreateStore();

        var run = await store.GetRunAsync("missing", CancellationToken.None);

        run.Should().BeNull();
    }

    [TestMethod]
    public async Task SaveRunAsync_ReplacesTheReportOfARunThatAlreadyHasOne()
    {
        var store = CreateStore();
        var run = Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc));

        await store.SaveRunAsync(run, "first", CancellationToken.None);
        await store.SaveRunAsync(run with { Status = "failed" }, "second", CancellationToken.None);

        var stored = await store.GetRunAsync("run-1", CancellationToken.None);
        var report = await store.GetRawReportAsync("run-1", CancellationToken.None);

        stored!.Status.Should().Be("failed");
        report.Should().Be("second");
    }

    [TestMethod]
    public async Task DeleteRunAsync_RemovesTheRunAndItsReport()
    {
        var store = CreateStore();
        await store.SaveRunAsync(Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)), "report", CancellationToken.None);

        await store.DeleteRunAsync("run-1", CancellationToken.None);

        (await store.GetRunAsync("run-1", CancellationToken.None)).Should().BeNull();
        (await store.GetRawReportAsync("run-1", CancellationToken.None)).Should().BeNull();
        (await store.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListAsync_SkipsAFileThatIsNotARun()
    {
        var store = CreateStore();
        await File.WriteAllTextAsync(Path.Combine(_directory, "broken.meta.json"), "{ not json");

        var runs = await store.ListAsync(CancellationToken.None);

        runs.Should().BeEmpty();
    }
}

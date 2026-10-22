using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions;
using APCS.Application.Features.TestReports;
using APCS.Application.Features.TestReports.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Application.UnitTests.Features.TestReports;

[TestClass]
public sealed class TestReportServiceTests
{
    private const string ReportJson = """
    {
      "config": {},
      "suites": [
        {
          "title": "landing.spec.ts",
          "file": "e2e/public/landing.spec.ts",
          "column": 0,
          "line": 0,
          "specs": [
            {
              "title": "shows the hero",
              "ok": true,
              "tags": [],
              "id": "1",
              "file": "e2e/public/landing.spec.ts",
              "line": 5,
              "column": 3,
              "tests": [
                {
                  "timeout": 30000,
                  "expectedStatus": "passed",
                  "projectId": "public",
                  "projectName": "public",
                  "results": [
                    { "status": "passed", "duration": 90, "retry": 0, "error": null, "errors": [] }
                  ]
                }
              ]
            }
          ],
          "suites": []
        }
      ],
      "errors": [],
      "stats": {
        "startTime": "2026-10-22T02:45:54.530Z",
        "duration": 90.0,
        "expected": 1,
        "unexpected": 0,
        "flaky": 0,
        "skipped": 0
      }
    }
    """;

    private readonly Mock<ITestReportStore> _store = new();
    private readonly Mock<ITestReportRunner> _runner = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly TestReportService _sut;

    public TestReportServiceTests()
    {
        _sut = new TestReportService(_store.Object, _runner.Object, _timeProvider);
    }

    private static TestReportRunDto Run(string runId, DateTime startedAtUtc, string status = "passed") => new()
    {
        RunId = runId,
        StartedAtUtc = startedAtUtc,
        Status = status,
    };

    [TestMethod]
    public async Task ListAsync_ReturnsTheRunsNewestFirst()
    {
        _store.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Run("run-old", new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc)),
            Run("run-new", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)),
        ]);

        var result = await _sut.ListAsync(new TestReportFilterDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Select(run => run.RunId).Should().Equal("run-new", "run-old");
        result.Value.TotalCount.Should().Be(2);
    }

    [TestMethod]
    public async Task ListAsync_KeepsOnlyTheRunsOfTheAskedRange()
    {
        _store.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Run("run-old", new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc)),
            Run("run-new", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)),
        ]);

        var result = await _sut.ListAsync(new TestReportFilterDto
        {
            FromUtc = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
        });

        result.Value.Items.Select(run => run.RunId).Should().Equal("run-new");
    }

    [TestMethod]
    public async Task ListAsync_KeepsOnlyTheRunsOfTheAskedState()
    {
        _store.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Run("run-failed", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc), "failed"),
            Run("run-passed", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)),
        ]);

        var result = await _sut.ListAsync(new TestReportFilterDto { Status = "failed" });

        result.Value.Items.Select(run => run.RunId).Should().Equal("run-failed");
    }

    [TestMethod]
    public async Task ListAsync_PutsTheRunsOnTheAskedPage()
    {
        _store.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)),
            Run("run-2", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)),
            Run("run-3", new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc)),
        ]);

        var result = await _sut.ListAsync(new TestReportFilterDto { PageIndex = 2, PageSize = 2 });

        result.Value.Items.Select(run => run.RunId).Should().Equal("run-3");
        result.Value.TotalCount.Should().Be(3);
        result.Value.TotalPages.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAsync_FailsWhenThereIsNoSuchRun()
    {
        _store.Setup(store => store.GetRunAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((TestReportRunDto?)null);

        var result = await _sut.GetAsync("missing");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("test_reports.not_found");
    }

    [TestMethod]
    public async Task GetAsync_ReturnsTheRunWithoutFilesWhileItIsStillGoing()
    {
        _store.Setup(store => store.GetRunAsync("run-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc), "running"));
        _store.Setup(store => store.GetRawReportAsync("run-1", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await _sut.GetAsync("run-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Run.Status.Should().Be("running");
        result.Value.Files.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_ReturnsTheTestsOfTheRun()
    {
        _store.Setup(store => store.GetRunAsync("run-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)));
        _store.Setup(store => store.GetRawReportAsync("run-1", It.IsAny<CancellationToken>())).ReturnsAsync(ReportJson);

        var result = await _sut.GetAsync("run-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Files.Should().HaveCount(1);
        result.Value.Files[0].File.Should().Be("e2e/public/landing.spec.ts");
        result.Value.Files[0].Specs[0].Title.Should().Be("shows the hero");
    }

    [TestMethod]
    public async Task StartRunAsync_KeepsTheRunWhileItGoesAndCountsWhatItFound()
    {
        _runner.Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestReportRunnerResult(1, "boom", ReportJson, "feat/fe-test-reports", "abc1234"));
        _store.Setup(store => store.SaveRunAsync(It.IsAny<TestReportRunDto>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var started = await _sut.StartRunAsync("admin@apcs.test");

        started.IsSuccess.Should().BeTrue();
        started.Value.Status.Should().Be("running");
        started.Value.TriggeredBy.Should().Be("admin@apcs.test");

        await WaitForTheRunToFinish();

        _store.Verify(
            store => store.SaveRunAsync(
                It.Is<TestReportRunDto>(run => run.Status == "passed" && run.PassedTests == 1 && run.Branch == "feat/fe-test-reports"),
                ReportJson,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StartRunAsync_KeepsTheRunAsBrokenWhenTheSuiteWroteNoReport()
    {
        _runner.Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestReportRunnerResult(1, "boom", null, null, null));
        _store.Setup(store => store.SaveRunAsync(It.IsAny<TestReportRunDto>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.StartRunAsync("admin@apcs.test");
        await WaitForTheRunToFinish();

        _store.Verify(
            store => store.SaveRunAsync(
                It.Is<TestReportRunDto>(run => run.Status == "error" && run.FailureSummary != null),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StartRunAsync_RefusesToStartASecondRunWhileOneGoes()
    {
        _runner.Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(50);
                return new TestReportRunnerResult(0, string.Empty, ReportJson, null, null);
            });
        _store.Setup(store => store.SaveRunAsync(It.IsAny<TestReportRunDto>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.StartRunAsync("admin@apcs.test");
        var second = await _sut.StartRunAsync("admin@apcs.test");

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("test_reports.run_in_progress");

        await WaitForTheRunToFinish();
    }

    [TestMethod]
    public async Task DeleteRunAsync_FailsWhenThereIsNoSuchRun()
    {
        _store.Setup(store => store.GetRunAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((TestReportRunDto?)null);

        var result = await _sut.DeleteRunAsync("missing");

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task DeleteRunAsync_RemovesTheRun()
    {
        _store.Setup(store => store.GetRunAsync("run-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Run("run-1", new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)));
        _store.Setup(store => store.DeleteRunAsync("run-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.DeleteRunAsync("run-1");

        result.IsSuccess.Should().BeTrue();
        _store.Verify(store => store.DeleteRunAsync("run-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task WaitForTheRunToFinish()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            await Task.Yield();
        }
    }
}

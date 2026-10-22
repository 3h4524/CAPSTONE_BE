using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Common.Constants;
using APCS.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.TestReports;

/// <summary>
/// The test runs the admin pages read, and the button that starts a new one.
/// </summary>
public interface ITestReportService
{
    /// <summary>
    /// Lists the runs that match the filter, newest first.
    /// </summary>
    Task<Result<PagedResult<TestReportRunDto>>> ListAsync(TestReportFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one run with the tests it covered.
    /// </summary>
    Task<Result<TestReportDetailDto>> GetAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a test run and returns it right away, while it is still going.
    /// </summary>
    Task<Result<TestReportRunDto>> StartRunAsync(string? triggeredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a run and its report.
    /// </summary>
    Task<Result<bool>> DeleteRunAsync(string runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The states a run can be in.
/// </summary>
public static class TestReportStatuses
{
    public const string Running = "running";
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Error = "error";
}

/// <summary>
/// Keeps the runs in the store and starts new ones.
/// </summary>
public sealed class TestReportService : ITestReportService
{
    private readonly ITestReportStore _store;
    private readonly ITestReportRunner _runner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TestReportService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _runLock = new();
    private string? _runningRunId;

    /// <summary>
    /// Creates the service.
    /// </summary>
    public TestReportService(
        ITestReportStore store,
        ITestReportRunner runner,
        IServiceScopeFactory scopeFactory,
        ILogger<TestReportService> logger,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _runner = runner;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<TestReportRunDto>>> ListAsync(TestReportFilterDto filter, CancellationToken cancellationToken = default)
    {
        var runs = await _store.ListAsync(cancellationToken);

        IEnumerable<TestReportRunDto> filtered = runs;
        if (filter.FromUtc is not null)
        {
            filtered = filtered.Where(run => run.StartedAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc is not null)
        {
            filtered = filtered.Where(run => run.StartedAtUtc <= filter.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            filtered = filtered.Where(run => string.Equals(run.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered.OrderByDescending(run => run.StartedAtUtc).ToList();
        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var items = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

        return Result.Success(new PagedResult<TestReportRunDto>(items, ordered.Count, pageIndex, pageSize));
    }

    /// <inheritdoc />
    public async Task<Result<TestReportDetailDto>> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _store.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<TestReportDetailDto>(Error.NotFound(ErrorCodes.TestReportNotFound, "No test run with that id exists."));
        }

        var rawReport = await _store.GetRawReportAsync(runId, cancellationToken);
        if (rawReport is null)
        {
            return Result.Success(new TestReportDetailDto { Run = run, Files = [] });
        }

        TestReportSummary summary;
        try
        {
            summary = PlaywrightReportParser.Parse(rawReport);
        }
        catch (Exception exception)
        {
            return Result.Failure<TestReportDetailDto>(Error.Failure(ErrorCodes.TestReportRunFailed, $"The report of this run cannot be read: {exception.Message}"));
        }

        return Result.Success(new TestReportDetailDto { Run = run, Files = summary.Files });
    }

    /// <inheritdoc />
    public async Task<Result<TestReportRunDto>> StartRunAsync(string? triggeredBy, CancellationToken cancellationToken = default)
    {
        // The suite signs in as a seller, and the person who starts the run has no seller
        // account to hand over, so one is prepared for the length of the run.
        TestSellerCredentials credentials;
        using (var scope = _scopeFactory.CreateScope())
        {
            var seller = scope.ServiceProvider.GetRequiredService<ITestSellerProvisioner>();
            credentials = await seller.EnsureAsync(cancellationToken);
        }

        TestReportRunDto run;
        lock (_runLock)
        {
            if (_runningRunId is not null)
            {
                return Result.Failure<TestReportRunDto>(Error.Conflict(ErrorCodes.TestReportRunInProgress, "A test run is already going."));
            }

            var startedAt = _timeProvider.GetUtcNow().UtcDateTime;
            run = new TestReportRunDto
            {
                RunId = $"run-{startedAt:yyyyMMdd-HHmmss}",
                StartedAtUtc = startedAt,
                Status = TestReportStatuses.Running,
                TriggeredBy = triggeredBy,
            };

            _runningRunId = run.RunId;
        }

        try
        {
            await _store.SaveRunAsync(run, null, cancellationToken);
        }
        catch
        {
            lock (_runLock)
            {
                _runningRunId = null;
            }

            throw;
        }

        _ = RunInBackgroundAsync(run, credentials);
        return Result.Success(run);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _store.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<bool>(Error.NotFound(ErrorCodes.TestReportNotFound, "No test run with that id exists."));
        }

        await _store.DeleteRunAsync(runId, cancellationToken);
        return Result.Success(true);
    }

    private async Task RunInBackgroundAsync(TestReportRunDto run, TestSellerCredentials credentials)
    {
        TestReportRunDto finished;
        string? rawReport = null;

        try
        {
            var result = await _runner.RunAsync(
                run.RunId,
                new Dictionary<string, string>
                {
                    ["E2E_SELLER_EMAIL"] = credentials.Email,
                    ["E2E_SELLER_PASSWORD"] = credentials.Password,
                },
                CancellationToken.None);
            rawReport = result.RawReport;

            if (rawReport is null)
            {
                finished = run with
                {
                    FinishedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    Status = TestReportStatuses.Error,
                    DurationMs = (long)_timeProvider.GetUtcNow().UtcDateTime.Subtract(run.StartedAtUtc).TotalMilliseconds,
                    Branch = result.Branch,
                    CommitSha = result.CommitSha,
                    FailureSummary = Summarize(result),
                };
            }
            else
            {
                var summary = PlaywrightReportParser.Parse(rawReport);
                finished = run with
                {
                    FinishedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    Status = summary.FailedTests > 0 ? TestReportStatuses.Failed : TestReportStatuses.Passed,
                    DurationMs = summary.DurationMs,
                    Branch = result.Branch,
                    CommitSha = result.CommitSha,
                    TotalTests = summary.TotalTests,
                    PassedTests = summary.PassedTests,
                    FailedTests = summary.FailedTests,
                    FlakyTests = summary.FlakyTests,
                    SkippedTests = summary.SkippedTests,
                    KnownIssueTests = summary.KnownIssueTests,
                    UnexpectedFailedTests = summary.FailedTests - summary.KnownIssueTests,
                    FileCount = summary.Files.Count,
                    SuiteCount = summary.Files.Sum(file => file.Specs.Count),
                };
            }
        }
        catch (Exception exception)
        {
            finished = run with
            {
                FinishedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                Status = TestReportStatuses.Error,
                FailureSummary = exception.Message,
            };
        }

        try
        {
            await _store.SaveRunAsync(finished, rawReport, CancellationToken.None);
        }
        finally
        {
            await RemoveTestSellerAsync();
            lock (_runLock)
            {
                _runningRunId = null;
            }
        }
    }

    private async Task RemoveTestSellerAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var seller = scope.ServiceProvider.GetRequiredService<ITestSellerProvisioner>();
            var removal = await seller.RemoveAsync(CancellationToken.None);
            if (removal.IsFailure)
            {
                _logger.LogWarning("The seller account of the test suite could not be removed: {Message}", removal.Error.Message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The seller account of the test suite could not be removed.");
        }
    }

    private static string Summarize(TestReportRunnerResult result)
    {
        var log = result.Log.Trim();
        if (log.Length == 0)
        {
            return $"The test command ended with code {result.ExitCode} and wrote no report.";
        }

        var tail = log.Length <= 600 ? log : log[^600..];
        return $"The test command ended with code {result.ExitCode}.{Environment.NewLine}{tail}";
    }
}

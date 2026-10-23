using APCS.Application.Features.TestReports.Dtos;

namespace APCS.Application.Abstractions;

/// <summary>
/// Where the test runs are kept. The file store is swapped for a database one once the runs
/// have to outlive the machine they were made on.
/// </summary>
public interface ITestReportStore
{
    /// <summary>
    /// Lists every run, newest first.
    /// </summary>
    Task<IReadOnlyList<TestReportRunDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one run, or null when there is no such run.
    /// </summary>
    Task<TestReportRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the report a run produced, as Playwright wrote it.
    /// </summary>
    Task<string?> GetRawReportAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps a run, with its report when the run already produced one.
    /// </summary>
    Task SaveRunAsync(TestReportRunDto run, string? rawReport, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a run and its report.
    /// </summary>
    Task DeleteRunAsync(string runId, CancellationToken cancellationToken = default);
}

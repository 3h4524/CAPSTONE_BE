namespace APCS.Application.Features.TestReports;

/// <summary>
/// Pushes the state of a test run to the connected admin pages as it changes.
/// </summary>
public interface ITestReportRunBroadcaster
{
    /// <summary>
    /// Tells the admins that the run with that id has landed in that state.
    /// </summary>
    Task PublishRunChangedAsync(string runId, string status, CancellationToken cancellationToken = default);
}

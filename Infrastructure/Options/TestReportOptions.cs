namespace APCS.Infrastructure.Options;

/// <summary>
/// Where the test runs live and how a run is made.
/// </summary>
public sealed class TestReportOptions
{
    /// <summary>
    /// The configuration section these options are read from.
    /// </summary>
    public const string SectionName = "TestReports";

    /// <summary>
    /// Gets or sets the folder the runs are kept in. Empty means a folder beside the repository.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder the test command runs in. Empty means the repository root.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command that runs the test suite.
    /// </summary>
    public string RunnerCommand { get; set; } = "bash scripts/e2e/run-all.sh e2e";

    /// <summary>
    /// Gets or sets the frontend folder, relative to the working directory.
    /// </summary>
    public string FrontendRoot { get; set; } = "../CAPSTONE_FE";

    /// <summary>
    /// Gets or sets where Playwright writes its JSON report, relative to the frontend folder.
    /// </summary>
    public string ReportFileName { get; set; } = "test-results/report/report.json";

    /// <summary>
    /// Gets or sets how long a run may take before it is stopped.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 90;
}

namespace APCS.Application.Features.TestReports.Dtos;

/// <summary>
/// The tests of one spec file, within one Playwright project.
/// </summary>
public sealed record TestReportFileDto
{
    /// <summary>
    /// Gets the path of the spec file, relative to the repository.
    /// </summary>
    public required string File { get; init; }

    /// <summary>
    /// Gets the Playwright project the file ran in.
    /// </summary>
    public required string Project { get; init; }

    /// <summary>
    /// Gets how long the file took, in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets the state of the file: passed, failed, flaky or skipped.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets how many tests of the file passed.
    /// </summary>
    public int Passed { get; init; }

    /// <summary>
    /// Gets how many tests of the file failed.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets how many tests of the file passed only after a retry.
    /// </summary>
    public int Flaky { get; init; }

    /// <summary>
    /// Gets how many tests of the file were skipped.
    /// </summary>
    public int Skipped { get; init; }

    /// <summary>
    /// Gets the tests of the file.
    /// </summary>
    public IReadOnlyList<TestReportSpecDto> Specs { get; init; } = [];
}

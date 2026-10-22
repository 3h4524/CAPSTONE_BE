using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class TestReportRun
{
    public Guid Id { get; set; }

    public string RunId { get; set; } = null!;

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public long DurationMs { get; set; }

    public string Status { get; set; } = null!;

    public string? Branch { get; set; }

    public string? CommitSha { get; set; }

    public string? TriggeredBy { get; set; }

    public int TotalTests { get; set; }

    public int PassedTests { get; set; }

    public int FailedTests { get; set; }

    public int FlakyTests { get; set; }

    public int SkippedTests { get; set; }

    public int KnownIssueTests { get; set; }

    public int UnexpectedFailedTests { get; set; }

    public int FileCount { get; set; }

    public int SuiteCount { get; set; }

    public string? FailureSummary { get; set; }

    public string? Report { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

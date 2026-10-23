using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Domain.Entities;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Keeps the test runs in the database, so a run outlives the machine that made it and every
/// deployment reads the same runs.
/// </summary>
public sealed class DatabaseTestReportStore(
    IRepository<TestReportRun> runs,
    IUnitOfWork unitOfWork) : ITestReportStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TestReportRunDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entities = await runs.GetAllAsync(cancellationToken);

        return entities
            .OrderByDescending(entity => entity.StartedAtUtc)
            .Select(Map)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<TestReportRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var entity = await FindAsync(runId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc />
    public async Task<string?> GetRawReportAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var entity = await FindAsync(runId, cancellationToken);
        return entity?.Report;
    }

    /// <inheritdoc />
    public async Task SaveRunAsync(
        TestReportRunDto run,
        string? rawReport,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(run.RunId, cancellationToken);

        if (entity is null)
        {
            entity = new TestReportRun { RunId = run.RunId };
            await runs.AddAsync(entity, saveChange: false, cancellationToken);
        }

        entity.StartedAtUtc = run.StartedAtUtc;
        entity.FinishedAtUtc = run.FinishedAtUtc;
        entity.DurationMs = run.DurationMs;
        entity.Status = run.Status;
        entity.Branch = run.Branch;
        entity.CommitSha = run.CommitSha;
        entity.TriggeredBy = run.TriggeredBy;
        entity.TotalTests = run.TotalTests;
        entity.PassedTests = run.PassedTests;
        entity.FailedTests = run.FailedTests;
        entity.FlakyTests = run.FlakyTests;
        entity.SkippedTests = run.SkippedTests;
        entity.KnownIssueTests = run.KnownIssueTests;
        entity.UnexpectedFailedTests = run.UnexpectedFailedTests;
        entity.FileCount = run.FileCount;
        entity.SuiteCount = run.SuiteCount;
        entity.FailureSummary = run.FailureSummary;

        if (rawReport is not null)
        {
            entity.Report = rawReport;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(runId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        await runs.RemoveAsync(entity, saveChange: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<TestReportRun?> FindAsync(string runId, CancellationToken cancellationToken)
    {
        var normalized = runId.Trim();

        // The run id is the natural key of a run, so the lookup goes by it rather than by the
        // surrogate key the database gave the row.
        var found = await runs.FindAsync(entity => entity.RunId == normalized, cancellationToken);
        return found.Count > 0 ? found[0] : null;
    }

    private static TestReportRunDto Map(TestReportRun entity) => new()
    {
        RunId = entity.RunId,
        StartedAtUtc = entity.StartedAtUtc,
        FinishedAtUtc = entity.FinishedAtUtc,
        DurationMs = entity.DurationMs,
        Status = entity.Status,
        Branch = entity.Branch,
        CommitSha = entity.CommitSha,
        TriggeredBy = entity.TriggeredBy,
        TotalTests = entity.TotalTests,
        PassedTests = entity.PassedTests,
        FailedTests = entity.FailedTests,
        FlakyTests = entity.FlakyTests,
        SkippedTests = entity.SkippedTests,
        KnownIssueTests = entity.KnownIssueTests,
        UnexpectedFailedTests = entity.UnexpectedFailedTests,
        FileCount = entity.FileCount,
        SuiteCount = entity.SuiteCount,
        FailureSummary = entity.FailureSummary,
    };
}

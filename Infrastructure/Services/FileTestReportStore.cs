using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Keeps the test runs as files: one small file per run and, once the run is over, the report
/// it produced. A database store takes over once the runs have to outlive the machine.
/// </summary>
public sealed class FileTestReportStore : ITestReportStore
{
    private const string MetaSuffix = ".meta.json";
    private const string ReportSuffix = ".report.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;

    /// <summary>
    /// Creates the store.
    /// </summary>
    public FileTestReportStore(IOptions<TestReportOptions> options)
    {
        var configured = options.Value.Directory;
        _directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(TestReportPaths.RepositoryRoot(), "test-reports")
            : Path.GetFullPath(configured);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestReportRunDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var runs = new List<TestReportRunDto>();

        foreach (var file in Directory.EnumerateFiles(_directory, $"*{MetaSuffix}"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await ReadRunAsync(file, cancellationToken);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        return runs.OrderByDescending(run => run.StartedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<TestReportRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var file = Path.Combine(_directory, $"{runId}{MetaSuffix}");
        return File.Exists(file) ? await ReadRunAsync(file, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<string?> GetRawReportAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var file = Path.Combine(_directory, $"{runId}{ReportSuffix}");
        if (!File.Exists(file))
        {
            return null;
        }

        return await File.ReadAllTextAsync(file, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveRunAsync(TestReportRunDto run, string? rawReport, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{run.RunId}{MetaSuffix}"),
            JsonSerializer.Serialize(run, SerializerOptions),
            cancellationToken);

        if (rawReport is null)
        {
            return;
        }

        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{run.RunId}{ReportSuffix}"),
            rawReport,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        foreach (var suffix in new[] { MetaSuffix, ReportSuffix })
        {
            var file = Path.Combine(_directory, $"{runId}{suffix}");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<TestReportRunDto?> ReadRunAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            return JsonSerializer.Deserialize<TestReportRunDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_directory))
        {
            Directory.CreateDirectory(_directory);
        }
    }
}

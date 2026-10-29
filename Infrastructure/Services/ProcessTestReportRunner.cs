using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Runs the test suite as a child process and brings back the report Playwright wrote.
/// </summary>
public sealed class ProcessTestReportRunner : ITestReportRunner
{
    private const int MaxLogLength = 200_000;

    private readonly TestReportOptions _options;
    private readonly string _workingDirectory;
    private readonly string _frontendDirectory;
    private readonly string _reportFile;

    /// <summary>
    /// Creates the runner.
    /// </summary>
    public ProcessTestReportRunner(IOptions<TestReportOptions> options)
    {
        _options = options.Value;
        var repositoryRoot = TestReportPaths.RepositoryRoot();
        _workingDirectory = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
            ? repositoryRoot
            : Path.GetFullPath(_options.WorkingDirectory);
        _frontendDirectory = Path.GetFullPath(Path.Combine(_workingDirectory, _options.FrontendRoot));
        _reportFile = Path.GetFullPath(Path.Combine(_frontendDirectory, _options.ReportFileName));
    }

    /// <inheritdoc />
    public async Task<TestReportRunnerResult> RunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var branch = ReadGit("rev-parse --abbrev-ref HEAD");
        var commit = ReadGit("rev-parse --short HEAD");

        if (File.Exists(_reportFile))
        {
            File.Delete(_reportFile);
        }

        var log = await RunCommandAsync(cancellationToken);
        var rawReport = File.Exists(_reportFile) ? await File.ReadAllTextAsync(_reportFile, cancellationToken) : null;

        return new TestReportRunnerResult(log.ExitCode, log.Output, rawReport, branch, commit);
    }

    private async Task<(int ExitCode, string Output)> RunCommandAsync(CancellationToken cancellationToken)
    {
        var isWindows = OperatingSystem.IsWindows();
        var startInfo = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "bash",
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (isWindows)
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
        }

        startInfo.ArgumentList.Add(_options.RunnerCommand);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, args) => Append(output, args.Data);
        process.ErrorDataReceived += (_, args) => Append(output, args.Data);

        if (!process.Start())
        {
            return (-1, $"The test command could not be started: {_options.RunnerCommand}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return (-1, $"{output}{Environment.NewLine}The test command was stopped after {_options.TimeoutMinutes} minutes.");
        }

        return (process.ExitCode, output.ToString());
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            if (output.Length > MaxLogLength)
            {
                return;
            }

            output.AppendLine(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private string? ReadGit(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _frontendDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(_frontendDirectory);
            foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}

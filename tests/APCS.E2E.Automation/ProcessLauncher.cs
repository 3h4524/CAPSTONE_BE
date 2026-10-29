using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace APCS.E2E;

public sealed record LaunchRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    string LogFilePath,
    string Description);

public sealed class LaunchedProcess : IDisposable
{
    private readonly Process _process;
    private LogWriter? _logWriter;

    public LaunchedProcess(Process process, string logFilePath, string description)
    {
        _process = process;
        LogFilePath = logFilePath;
        Description = description;
    }

    public string Description { get; }

    public string LogFilePath { get; }

    public Process Process => _process;

    public Process? Inner
    {
        get
        {
            try
            {
                _process.Refresh();
                return _process.HasExited ? null : _process;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public int Pid => _process.Id;

    internal void AttachLogWriter(LogWriter writer) => Interlocked.Exchange(ref _logWriter, writer)?.Dispose();

    internal void DisposeLogWriter()
    {
        Interlocked.Exchange(ref _logWriter, null)?.Dispose();
    }

    public void KillTree()
    {
        try
        {
            _process.Refresh();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(15));
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            DisposeLogWriter();
        }
    }

    public void Dispose() => DisposeLogWriter();
}

public static class ProcessLauncher
{
    public static LaunchedProcess StartScript(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string logFilePath,
        string description) =>
        StartShell(command, workingDirectory, environment, logFilePath, description);

    public static LaunchedProcess StartShell(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string logFilePath,
        string description)
    {
        var arguments = new List<string> { "-lc", command };

        return StartCore(new LaunchRequest(
            "/bin/bash",
            arguments,
            workingDirectory,
            environment,
            logFilePath,
            description));
    }

    public static LaunchedProcess Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string logFilePath,
        string description) =>
        StartCore(new LaunchRequest(
            fileName,
            arguments,
            workingDirectory,
            environment,
            logFilePath,
            description));

    public static async Task<(int ExitCode, string LogTail)> WaitForExitAsync(
        LaunchedProcess launched,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        string timeoutTail;

        try
        {
            await launched.Process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timeoutTail = await ReadTailAsync(launched.LogFilePath, CancellationToken.None);

            if (timeoutSource.IsCancellationRequested)
            {
                launched.KillTree();
                return (-1, $"The process '{launched.Description}' exceeded its timeout.\n{timeoutTail}");
            }

            launched.KillTree();
            return (-1, $"The process '{launched.Description}' was cancelled.\n{timeoutTail}");
        }

        launched.Process.WaitForExit();
        launched.DisposeLogWriter();

        var exitCode = launched.Process.ExitCode;
        return (exitCode, await ReadTailAsync(launched.LogFilePath, CancellationToken.None));
    }

    public static async Task<string> ReadTailAsync(string logFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var lines = new List<string>();
        await using var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
        }

        return lines.Count <= 400
            ? string.Join(Environment.NewLine, lines)
            : string.Join(Environment.NewLine, lines.GetRange(lines.Count - 400, 400));
    }

    public static void KillPidTree(int pid)
    {
        foreach (var child in ReadChildren(pid))
        {
            KillPidTree(child);
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }
    }

    public static void KillPidFromFile(string pidFilePath)
    {
        if (!File.Exists(pidFilePath) || !int.TryParse(File.ReadAllText(pidFilePath).Trim(), out var pid))
        {
            return;
        }

        KillPidTree(pid);
    }

    private static LaunchedProcess StartCore(LaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        var logDirectory = Path.GetDirectoryName(Path.GetFullPath(request.LogFilePath));
        if (logDirectory is not null)
        {
            Directory.CreateDirectory(logDirectory);
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"The process '{request.Description}' could not be started.");
        }

        var launched = new LaunchedProcess(process, request.LogFilePath, request.Description);
        var writer = new LogWriter(request.LogFilePath, request.Description);
        launched.AttachLogWriter(writer);

        process.OutputDataReceived += (_, args) => writer.Write(args.Data);
        process.ErrorDataReceived += (_, args) => writer.Write(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return launched;
    }

    private static IReadOnlyList<int> ReadChildren(int pid)
    {
        var childrenPath = $"/proc/{pid}/task/{pid}/children";

        if (!File.Exists(childrenPath))
        {
            return [];
        }

        var content = File.ReadAllText(childrenPath);
        var children = new List<int>();

        foreach (var token in content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var child))
            {
                children.Add(child);
            }
        }

        return children;
    }
}

internal sealed class LogWriter : IDisposable
{
    private readonly string _prefix;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LogWriter(string logFilePath, string prefix)
    {
        _writer = new StreamWriter(
            new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        {
            AutoFlush = true,
        };
        _prefix = $"[{prefix}] ";
    }

    public void Write(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _writer.WriteLine($"{_prefix}{line}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}

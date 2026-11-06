using System.Diagnostics;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Reads the text of a design with PaddleOCR in a Python worker that stays open (<c>tools/design-text-check/ocr_worker.py</c>),
/// so its models load once instead of for every image. One image at a time; the worker is started again when it stops.
/// Any failure gives null: the text check only ever warns.
/// </summary>
public sealed class PaddleOcrDesignTextReader(
    IOptions<DesignTextCheckOptions> options,
    IHostEnvironment environment,
    ILogger<PaddleOcrDesignTextReader> logger) : IDesignTextReader, IDisposable
{
    private const string AnswerPrefix = "@@OCR ";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _worker;
    private DateTime _startAgainAfterUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<DesignTextLine>?> ReadAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) return null;

        await _gate.WaitAsync(cancellationToken);
        var file = Path.Combine(Path.GetTempPath(), $"apcs-design-text-{Guid.NewGuid():N}.png");
        try
        {
            var worker = await WorkerAsync(settings, cancellationToken);
            if (worker is null) return null;

            await File.WriteAllBytesAsync(file, image, cancellationToken);
            var id = Guid.NewGuid().ToString("N");
            await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, path = file }));
            await worker.StandardInput.FlushAsync(cancellationToken);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 600)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            JsonElement answer;
            try
            {
                answer = await AnswerAsync(worker, id, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("The OCR worker took longer than {Seconds}s on a design; it is started again for the next one.",
                    settings.TimeoutSeconds);
                StopWorker();
                return null;
            }

            if (answer.TryGetProperty("error", out var error))
            {
                logger.LogWarning("The OCR worker could not read a design: {Error}", error.GetString());
                return null;
            }
            return answer.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array
                ? lines.EnumerateArray()
                    .Select(line => new DesignTextLine(
                        line.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty,
                        line.TryGetProperty("score", out var score) && score.TryGetDouble(out var value) ? value : 0))
                    .ToList()
                : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The OCR check of a design failed; the design is kept without it.");
            StopWorker();
            return null;
        }
        finally
        {
            TryDelete(file);
            _gate.Release();
        }
    }

    // The running worker, started (and waited for until its models are loaded) when there is none.
    private async Task<Process?> WorkerAsync(DesignTextCheckOptions settings, CancellationToken cancellationToken)
    {
        if (_worker is { HasExited: false } running) return running;
        StopWorker();
        if (DateTime.UtcNow < _startAgainAfterUtc) return null;

        var script = BiRefNetGarmentSegmenter.ResolvePath(settings.ScriptPath, environment.ContentRootPath);
        if (script is null)
        {
            logger.LogWarning("The OCR worker script '{Script}' was not found; designs are not text-checked.", settings.ScriptPath);
            _startAgainAfterUtc = DateTime.UtcNow.AddMinutes(settings.RetryAfterMinutes);
            return null;
        }

        var start = new ProcessStartInfo
        {
            FileName = settings.PythonExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--lang");
        start.ArgumentList.Add(settings.Language);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUNBUFFERED"] = "1";

        var worker = new Process { StartInfo = start };
        try
        {
            if (!worker.Start()) throw new InvalidOperationException("The process did not start.");
            // Read all the time, so a worker printing a lot is never stuck on a full pipe.
            worker.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) logger.LogDebug("OCR worker: {Line}", e.Data);
            };
            worker.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.StartupTimeoutSeconds, 10, 900)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await AnswerAsync(worker, null, linked.Token);
            _worker = worker;
            logger.LogInformation("The OCR worker for the design text check is ready.");
            return worker;
        }
        catch (Exception ex)
        {
            Kill(worker);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            logger.LogWarning(ex, "The OCR worker could not start ({Python} {Script}); designs are not text-checked for {Minutes} minutes.",
                settings.PythonExecutable, script, settings.RetryAfterMinutes);
            _startAgainAfterUtc = DateTime.UtcNow.AddMinutes(settings.RetryAfterMinutes);
            return null;
        }
    }

    // The worker's answer to request id, or its ready message when id is null. Answers to earlier requests that were given
    // up on (the caller was cancelled) are skipped.
    private static async Task<JsonElement> AnswerAsync(Process worker, string? id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await worker.StandardOutput.ReadLineAsync(cancellationToken)
                ?? throw new InvalidOperationException("The OCR worker stopped.");
            if (!line.StartsWith(AnswerPrefix, StringComparison.Ordinal)) continue;

            using var json = JsonDocument.Parse(line[AnswerPrefix.Length..]);
            var answer = json.RootElement.Clone();
            if (id is null)
            {
                if (answer.TryGetProperty("ready", out _)) return answer;
                continue;
            }
            if (answer.TryGetProperty("id", out var answerId) && answerId.GetString() == id) return answer;
        }
    }

    private void StopWorker()
    {
        if (_worker is null) return;
        Kill(_worker);
        _worker = null;
    }

    private static void Kill(Process worker)
    {
        try
        {
            if (!worker.HasExited) worker.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // Never started, or already gone.
        }
        worker.Dispose();
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        StopWorker();
        _gate.Dispose();
    }
}

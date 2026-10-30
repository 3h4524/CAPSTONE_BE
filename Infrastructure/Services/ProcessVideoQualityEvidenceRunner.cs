using System.Diagnostics;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Downloads an already-generated scene clip and invokes locally installed quality-tool adapters.</summary>
public sealed class ProcessVideoQualityEvidenceRunner(IOptions<VideoQualityOptions> options,
    IHttpClientFactory clients) : IVideoQualityEvidenceRunner
{
    private const int MaximumOutputLength = 20_000;

    public async Task<VideoQualityEvidenceResult> EvaluateAsync(VideoQualityEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return new("skipped", "video-quality-v1", new Dictionary<string, object?>(),
                "Local PaddleOCR/VBench evaluation is disabled.");

        var workspace = Directory.CreateTempSubdirectory("apcs-video-quality-");
        try
        {
            var clip = Path.Combine(workspace.FullName, "scene.mp4");
            var source = Path.Combine(workspace.FullName, "source.png");
            await DownloadAsync(request.ClipUrl, clip, cancellationToken);
            await File.WriteAllBytesAsync(source, request.SourceImage, cancellationToken);

            var metrics = new Dictionary<string, object?>();
            var attempted = 0;
            var completed = 0;
            var uncertain = 0;
            var failures = new List<string>();
            foreach (var tool in new[] { (Name: "paddleOcr", Script: settings.PaddleOcrScriptPath), (Name: "vbench", Script: settings.VBenchScriptPath) })
            {
                if (string.IsNullOrWhiteSpace(tool.Script))
                {
                    metrics[tool.Name] = new Dictionary<string, object?> { ["status"] = "skipped", ["reason"] = "No script configured." };
                    continue;
                }
                attempted++;
                var output = Path.Combine(workspace.FullName, $"{tool.Name}.json");
                var result = await RunToolAsync(settings, tool.Script, clip, source, output, cancellationToken);
                if (result.Error is not null)
                {
                    metrics[tool.Name] = new Dictionary<string, object?> { ["status"] = "failed", ["reason"] = result.Error };
                    failures.Add($"{tool.Name}: {result.Error}");
                    continue;
                }
                metrics[tool.Name] = result.Metrics!;
                var toolStatus = ReadStatus(result.Metrics!);
                if (toolStatus == "failed")
                {
                    failures.Add($"{tool.Name}: the adapter reported failure.");
                    continue;
                }
                completed++;
                if (toolStatus == "uncertain") uncertain++;
            }

            if (attempted == 0)
                return new("skipped", "video-quality-v1", metrics, "No local PaddleOCR or VBench script is configured.");
            return completed == attempted && uncertain == 0
                ? new("completed", "video-quality-v1", metrics)
                : new(completed > 0 ? "uncertain" : "failed", "video-quality-v1", metrics, string.Join(" ", failures));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new("failed", "video-quality-v1", new Dictionary<string, object?>(), exception.Message);
        }
        finally
        {
            try { workspace.Delete(true); } catch (IOException) { }
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient("PrivateMedia").GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static async Task<ToolResult> RunToolAsync(VideoQualityOptions settings, string script, string clip,
        string source, string output, CancellationToken cancellationToken)
    {
        if (!File.Exists(script))
            return new(null, $"Configured script was not found: {script}");
        var start = new ProcessStartInfo
        {
            FileName = settings.PythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--video"); start.ArgumentList.Add(clip);
        start.ArgumentList.Add("--source"); start.ArgumentList.Add(source);
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            return new(null, "Quality tool could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 600)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            return new(null, "Quality tool timed out or was cancelled.");
        }
        var outputText = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
            return new(null, Truncate(outputText, $"Quality tool exited with code {process.ExitCode}."));
        if (!File.Exists(output))
            return new(null, "Quality tool did not write its JSON output file.");
        try
        {
            var json = await File.ReadAllTextAsync(output, cancellationToken);
            return new(JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>(), null);
        }
        catch (JsonException)
        {
            return new(null, "Quality tool output is not valid JSON.");
        }
    }

    private static string Truncate(string output, string fallback) => string.IsNullOrWhiteSpace(output)
        ? fallback
        : output.Length <= MaximumOutputLength ? output.Trim() : output[..MaximumOutputLength].Trim();

    private static string? ReadStatus(IReadOnlyDictionary<string, object?> metrics)
    {
        if (!metrics.TryGetValue("status", out var value)) return null;
        return value is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim().ToLowerInvariant()
            : value?.ToString()?.Trim().ToLowerInvariant();
    }

    private sealed record ToolResult(IReadOnlyDictionary<string, object?>? Metrics, string? Error);
}

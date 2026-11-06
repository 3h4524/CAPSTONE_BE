namespace APCS.Infrastructure.Options;

/// <summary>Optional local PaddleOCR and VBench entry points consumed by automatic scene evaluation.</summary>
public sealed class VideoQualityOptions
{
    public const string SectionName = "VideoQuality";
    public bool Enabled { get; set; }
    public string? WorkerUrl { get; set; }
    public string? WorkerToken { get; set; }
    public int WorkerTimeoutSeconds { get; set; } = 3600;
    public string PythonExecutable { get; set; } = "python";
    public string? PaddleOcrScriptPath { get; set; }
    public string? VBenchScriptPath { get; set; }
    public int TimeoutSeconds { get; set; } = 90;
    /// <summary>Claims queued automatic scene-evaluation jobs. Disabled local tooling records a skipped result.</summary>
    public bool AutoEvaluate { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 3;
}

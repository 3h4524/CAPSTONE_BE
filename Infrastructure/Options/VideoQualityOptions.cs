namespace APCS.Infrastructure.Options;

/// <summary>Optional local PaddleOCR and VBench entry points used only after a seller requests evaluation.</summary>
public sealed class VideoQualityOptions
{
    public const string SectionName = "VideoQuality";
    public bool Enabled { get; set; }
    public string PythonExecutable { get; set; } = "python";
    public string? PaddleOcrScriptPath { get; set; }
    public string? VBenchScriptPath { get; set; }
    public int TimeoutSeconds { get; set; } = 90;
}

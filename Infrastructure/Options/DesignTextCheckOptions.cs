namespace APCS.Infrastructure.Options;

/// <summary>
/// The OCR check of the text in each generated design (PaddleOCR, run by <c>tools/design-text-check/ocr_worker.py</c>).
/// Off by default: it needs a Python environment with PaddleOCR. A design is never held back by the check.
/// </summary>
public sealed class DesignTextCheckOptions
{
    public const string SectionName = "DesignTextCheck";

    /// <summary>Read only at startup: the check is wired in when the API starts with it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Python that has PaddleOCR installed (paddleocr 3.1, paddlex 3.1).</summary>
    public string PythonExecutable { get; set; } = "python";

    /// <summary>The worker script; a relative path is looked for from the API folder and from its parent.</summary>
    public string ScriptPath { get; set; } = "tools/design-text-check/ocr_worker.py";

    /// <summary>The PaddleOCR language model.</summary>
    public string Language { get; set; } = "en";

    /// <summary>How long the worker may take to load its models when it starts.</summary>
    public int StartupTimeoutSeconds { get; set; } = 300;

    /// <summary>How long reading one image may take (about 11 s on a laptop CPU).</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>How long to wait before starting the worker again after it could not start.</summary>
    public int RetryAfterMinutes { get; set; } = 5;
}

namespace APCS.Application.Features.Workflows;

/// <summary>One transparent Etsy listing-video policy result.</summary>
public sealed record EtsyComplianceCheck(string Key, string Status, string Detail);
/// <summary>Separates Etsy upload eligibility from stricter renderer-specific technical checks.</summary>
public sealed record EtsyComplianceResult(string Status, IReadOnlyList<EtsyComplianceCheck> Checks);

public static class EtsyVideoCompliance
{
    private const long MaximumBytes = 100L * 1024 * 1024;

    public static EtsyComplianceResult Evaluate(MediaQa qa, string aspectRatio, string fileFormat)
    {
        var checks = new List<EtsyComplianceCheck>
        {
            new("decode", qa.FullDecode ? "pass" : "fail", qa.FullDecode ? "The complete file decoded successfully." : "The complete file could not be decoded."),
            new("duration", qa.DurationSeconds is >= VideoDurationPolicy.MinimumVideoSeconds and <= VideoDurationPolicy.MaximumVideoSeconds
                ? "pass" : "fail", $"{qa.DurationSeconds:0.##} seconds; policy is 5–15 seconds."),
            new("fileSize", qa.Bytes is > 0 and <= MaximumBytes ? "pass" : "fail", $"{qa.Bytes / (1024d * 1024d):0.##} MB; maximum is 100 MB."),
            new("format", IsSupportedFormat(fileFormat) ? "pass" : "fail", $"{fileFormat.ToUpperInvariant()} is {(IsSupportedFormat(fileFormat) ? "an accepted" : "not an accepted")} listing video format."),
            new("resolution", Math.Min(qa.Width, qa.Height) >= 500 ? (Math.Min(qa.Width, qa.Height) >= 1080 ? "pass" : "warning") : "fail",
                $"{qa.Width}×{qa.Height}; Etsy requires at least 500 px and recommends at least 1080 px."),
            new("audio", qa.HasAudio ? "info" : "pass", qa.HasAudio ? "Etsy removes audio after upload." : "No audio track is present.")
        };
        var status = checks.Any(check => check.Status == "fail") ? "fail" : checks.Any(check => check.Status == "warning") ? "pass_with_warning" : "pass";
        return new(status, checks);
    }

    /// <summary>
    /// What <see cref="Evaluate"/> says about the video the storyboard will render, before any file exists: the size, length and
    /// sound are the plan's, and the file is taken to decode and to fit the size limit. The real file is checked again when it is made.
    /// </summary>
    public static EtsyComplianceResult EvaluatePlanned(Storyboard board)
    {
        var output = VideoOutputFormats.Resolve(board.OutputFormat);
        var width = board.Width > 0 ? board.Width : output.Width;
        var height = board.Height > 0 ? board.Height : output.Height;
        var seconds = (board.OutputDurationFrames ?? board.DurationSeconds * 30) / 30d;
        var plannedQa = new MediaQa("h264", "yuv420p", width, height, 30, seconds, Bytes: 1, board.VideoEdit?.Music is { Muted: false }, FullDecode: true);
        return Evaluate(plannedQa, board.AspectRatio, "mp4");
    }

    private static bool IsSupportedFormat(string format) => format.Trim().ToLowerInvariant() is "mp4" or "mov" or "flv" or "aac" or "avi" or "3gp" or "mpeg";
}

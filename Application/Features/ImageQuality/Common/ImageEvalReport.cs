using System.Text.Json;
using APCS.Application.Features.ImageQuality.Dtos.Response;

namespace APCS.Application.Features.ImageQuality.Common;

/// <summary>
/// What <c>image_eval_runs.report</c> holds: a run's prompt versions and scored images, with thumbnail URLs in place of
/// the thumbnails uploaded with it.
/// </summary>
public sealed record ImageEvalReport(
    IReadOnlyList<ImageEvalVersionResponseDto> Versions,
    IReadOnlyList<ImageEvalImageResponseDto> Images)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The report as stored.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads a stored report; an empty one when the column holds nothing usable.</summary>
    public static ImageEvalReport FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            var report = JsonSerializer.Deserialize<ImageEvalReport>(json, JsonOptions);
            return report is null
                ? Empty
                : report with { Versions = report.Versions ?? [], Images = report.Images ?? [] };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static ImageEvalReport Empty { get; } = new([], []);
}

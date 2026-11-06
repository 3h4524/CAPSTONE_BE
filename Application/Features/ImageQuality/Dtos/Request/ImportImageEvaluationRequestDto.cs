namespace APCS.Application.Features.ImageQuality.Dtos.Request;

/// <summary>
/// One offline scoring run as the export step of the offline scoring scripts (kept outside the repo) writes it: the prompt versions it compares
/// and every scored image.
/// </summary>
/// <param name="Name">What the admin calls the run.</param>
/// <param name="Notes">What changed in the prompt and which images were scored.</param>
/// <param name="Versions">The prompt versions the images belong to.</param>
/// <param name="Images">Every scored image.</param>
public sealed record ImportImageEvaluationRequestDto(
    string Name,
    string? Notes,
    IReadOnlyList<ImageEvalVersionRequestDto> Versions,
    IReadOnlyList<ImageEvalImageRequestDto> Images);

/// <summary>A prompt version of a scoring run.</summary>
/// <param name="Key">The version's key; images name it.</param>
/// <param name="Label">What the admin pages call it.</param>
/// <param name="Provider">The image provider that made its images.</param>
/// <param name="Phase"><c>before</c>, <c>after</c> or <c>trial</c> (tested and not kept).</param>
/// <param name="Order">Its place in time; versions of one generation share it.</param>
public sealed record ImageEvalVersionRequestDto(
    string Key,
    string Label,
    string Provider,
    string Phase,
    int Order);

/// <summary>One scored image. Rates are null where a check does not apply, scores where a model gave none.</summary>
/// <param name="Thumbnail">A small <c>data:image/...;base64,</c> copy, stored with the run so the page can show it.</param>
public sealed record ImageEvalImageRequestDto(
    string Id,
    string Version,
    string Product,
    string? ProductType,
    string? ExpectedText,
    string? ImageUrl,
    string? Thumbnail,
    string? OcrText,
    bool? TextOk,
    bool? TextOkOcr,
    bool? TextClear,
    string? Note,
    double? Cer,
    bool? LayoutOk,
    bool? NotCut,
    bool? Centred,
    bool? SizeOk,
    double? Musiq,
    double? TopiqNr,
    double? ClipIqaPlus,
    double? Liqe,
    double? LaionAes,
    double? ClipOverall,
    double? ClipElements);

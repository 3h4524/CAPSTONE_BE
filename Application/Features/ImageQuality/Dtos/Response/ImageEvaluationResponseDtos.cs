namespace APCS.Application.Features.ImageQuality.Dtos.Response;

/// <summary>A stored scoring run without its images.</summary>
public sealed record ImageEvaluationSummaryResponseDto(
    Guid Id,
    string Name,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    string? UploadedBy,
    int ImageCount,
    int ProductCount,
    IReadOnlyList<ImageEvalVersionResponseDto> Versions);

/// <summary>A stored scoring run with every scored image.</summary>
public sealed record ImageEvaluationDetailResponseDto(
    Guid Id,
    string Name,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    string? UploadedBy,
    int ImageCount,
    int ProductCount,
    IReadOnlyList<ImageEvalVersionResponseDto> Versions,
    IReadOnlyList<ImageEvalImageResponseDto> Images);

/// <summary>A prompt version of a stored run, with how many of its images were scored.</summary>
public sealed record ImageEvalVersionResponseDto(
    string Key,
    string Label,
    string Provider,
    string Phase,
    int Order,
    int ImageCount);

/// <summary>One scored image of a stored run; <see cref="ThumbnailUrl"/> is the copy kept with the run.</summary>
public sealed record ImageEvalImageResponseDto(
    string Id,
    string Version,
    string Product,
    string? ProductType,
    string? ExpectedText,
    string? ImageUrl,
    string? ThumbnailUrl,
    string OcrText,
    bool? TextOk,
    bool? TextOkOcr,
    bool? TextClear,
    string Note,
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

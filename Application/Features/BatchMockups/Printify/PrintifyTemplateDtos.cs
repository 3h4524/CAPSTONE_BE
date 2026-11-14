namespace APCS.Application.Features.BatchMockups.Printify;

/// <summary>
/// Starts measuring a Printify product to make a template of it: the sides to photograph (front, back, left, right)
/// and the garment colors to measure. A template made from it must have the front.
/// </summary>
public sealed record StartPrintifyTemplateJobRequestDto(
    long BlueprintId,
    long PrintProviderId,
    IReadOnlyList<string> Views,
    IReadOnlyList<string>? Colors = null);

/// <summary>
/// For a template already linked to Printify: sides to add or photograph again (not the front, which is the template
/// itself), and more colors to measure.
/// </summary>
public sealed record StartPrintifyTemplateUpdateRequestDto(IReadOnlyList<string> Views, IReadOnlyList<string>? Colors = null);

/// <summary>
/// Makes a template from a finished measuring job, or (for a job on a linked template) adds its sides and colors.
/// <c>Cameras</c> picks one measured camera per side; <c>Colors</c> are the measured colors to keep.
/// <c>Name</c> and <c>ProductType</c> are needed for a new template only.
/// </summary>
public sealed record CreatePrintifyTemplateRequestDto(
    IReadOnlyDictionary<string, string> Cameras,
    IReadOnlyList<string> Colors,
    string? Name = null,
    string? ProductType = null,
    IReadOnlyList<string>? DefaultSizes = null);


/// <summary>A measuring job and, once done, what it found. Kept for an hour.</summary>
/// <param name="Status">"queued", "running", "done" or "failed".</param>
/// <param name="Step">What it is doing now, in words.</param>
public sealed record PrintifyTemplateJobDto(
    Guid JobId,
    string Status,
    string? Step,
    string? Error,
    long BlueprintId,
    string BlueprintTitle,
    string? BlueprintImageUrl,
    long PrintProviderId,
    string PrintProviderTitle,
    Guid? TemplateId,
    IReadOnlyList<string> Views,
    IReadOnlyList<string> Colors,
    IReadOnlyList<PrintifySideResultDto> Sides,
    IReadOnlyList<PrintifyMeasuredColorDto> MeasuredColors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Sizes,
    DateTimeOffset CreatedAt);

/// <summary>The cameras of one side whose print area was found, best first; <c>Problem</c> says why there is none.</summary>
public sealed record PrintifySideResultDto(string View, string Position, IReadOnlyList<PrintifyCameraCandidateDto> Candidates, string? Problem);

/// <summary>
/// One Printify camera of a side: its bare white photo (kept for an hour) and the print area found on it, in its pixels.
/// </summary>
public sealed record PrintifyCameraCandidateDto(string Camera, string PhotoUrl, int PhotoWidth, int PhotoHeight, int X, int Y, int Width, int Height);

/// <summary>A garment color as Printify photographs it, and the color that recolors the white photo to it.</summary>
public sealed record PrintifyMeasuredColorDto(string Name, string PhotoHex, string RecolorHex);

/// <summary>A garment color of a blueprint as mock-ups use it; <c>Shared</c> rows are used by every blueprint.</summary>
public sealed record PrintifyColorRowDto(Guid Id, string Name, string Hex, long? BlueprintId, bool Shared);

/// <summary>Adds or changes a blueprint's color: <c>Name</c> must be one of the blueprint's Printify colors.</summary>
public sealed record SavePrintifyColorRequestDto(long BlueprintId, string Name, string Hex);

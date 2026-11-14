using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Dtos.Response;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Creates a personal mock-up template with a real uploaded base photo.</summary>
/// <remarks>
/// <c>Areas</c>, when given, are all the template's print areas (the main one keyed "main") and X/Y/Width/Height
/// are not used; otherwise X/Y/Width/Height are its one print area. <c>View</c> is the side of the garment the photo
/// shows: "front" (the default), "back" or "side". With <c>MainTemplateId</c> the photo is another side of that
/// template (back, left or right sleeve, named by <c>View</c>): it is linked to it in the same save and is not listed
/// on its own. <c>FromPrintify</c> is set only by the server, for a photo measured on Printify: it may be a side of a
/// Printify template, which an uploaded photo never is.
/// </remarks>
public sealed record CreateMockupTemplateRequestDto(
    string Name,
    string ProductType,
    int X,
    int Y,
    int Width,
    int Height,
    UploadFileDto? BaseImage,
    bool AllowRecolor = false,
    string? GarmentColor = null,
    string? View = null,
    IReadOnlyList<MockupAreaDto>? Areas = null,
    Guid? MainTemplateId = null,
    bool FromPrintify = false);

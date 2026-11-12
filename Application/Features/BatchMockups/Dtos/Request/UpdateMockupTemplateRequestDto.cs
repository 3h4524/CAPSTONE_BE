using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Dtos.Response;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Updates a personal mock-up template. <see cref="BaseImage"/> is optional — omit it to keep the existing photo.</summary>
/// <remarks>Areas and View work as on <see cref="CreateMockupTemplateRequestDto"/>.</remarks>
public sealed record UpdateMockupTemplateRequestDto(
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
    IReadOnlyList<MockupAreaDto>? Areas = null);

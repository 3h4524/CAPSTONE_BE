using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Updates a personal mock-up template. <see cref="BaseImage"/> is optional — omit it to keep the existing photo.</summary>
public sealed record UpdateMockupTemplateRequestDto(
    string Name,
    string ProductType,
    int X,
    int Y,
    int Width,
    int Height,
    UploadFileDto? BaseImage);

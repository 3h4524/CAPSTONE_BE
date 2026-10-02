using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Creates a personal mock-up template with a real uploaded base photo.</summary>
public sealed record CreateMockupTemplateRequestDto(
    string Name,
    string ProductType,
    int X,
    int Y,
    int Width,
    int Height,
    UploadFileDto? BaseImage);

using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Creates or changes a model photo: a person wearing a plain, light garment that mock-ups are put on.</summary>
/// <param name="Name">The display name.</param>
/// <param name="ProductType">The product type the garment is.</param>
/// <param name="X">Left of the print area, in the photo's pixels.</param>
/// <param name="Y">Top of the print area.</param>
/// <param name="Width">Width of the print area.</param>
/// <param name="Height">Height of the print area.</param>
/// <param name="Photo">The photo; required to create, optional to change (omit it to keep the photo).</param>
/// <param name="GarmentSelection">
/// A PNG whose alpha is the garment on the photo, at its size or scaled down from it. Required with a new photo
/// unless <paramref name="FindGarment"/>; on its own it replaces the garment selected before.
/// </param>
/// <param name="AutoPlacement">
/// Place each design by measuring the garments, as it sits on its mock-up; the print area is then only a fallback.
/// False places it in the print area.
/// </param>
/// <param name="PrintQuad">
/// The print area's four corners (top-left, top-right, bottom-right, bottom-left) in the photo's pixels, for a
/// chest not square to the camera; the design is warped onto them. Null keeps the print area a rectangle. Not
/// used with <paramref name="AutoPlacement"/>.
/// </param>
/// <param name="FindGarment">
/// Find the garment on the photo automatically instead of taking <paramref name="GarmentSelection"/>; without a new
/// photo, finds it again on the saved one.
/// </param>
/// <param name="Attributes">Who is in the photo, for filtering the library; null keeps what it had.</param>
public sealed record SaveModelPhotoRequestDto(
    string Name,
    string ProductType,
    int X,
    int Y,
    int Width,
    int Height,
    UploadFileDto? Photo,
    UploadFileDto? GarmentSelection,
    bool AutoPlacement = false,
    IReadOnlyList<MockupPoint>? PrintQuad = null,
    bool FindGarment = false,
    ModelAttributesDto? Attributes = null);

/// <summary>Makes a model photo with AI, of the people described, wearing a plain white t-shirt.</summary>
/// <param name="ProductType">The product type the garment is.</param>
/// <param name="Attributes">Who is in the photo: one person of an age group and gender, or a group.</param>
public sealed record GenerateModelPhotoRequestDto(string ProductType, ModelAttributesDto Attributes);

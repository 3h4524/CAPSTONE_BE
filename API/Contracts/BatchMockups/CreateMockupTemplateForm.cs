using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.BatchMockups;

/// <summary>Multipart form used to create a personal mock-up template.</summary>
public sealed class CreateMockupTemplateForm
{
    public string Name { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public IFormFile? BaseImage { get; set; }
    public bool AllowRecolor { get; set; }
    public string? GarmentColor { get; set; }

    /// <summary>The side of the garment the photo shows: "front" (the default), "back" or "side".</summary>
    public string? View { get; set; }

    /// <summary>
    /// Every print area as a JSON array of { key, name, x, y, width, height, rotation, source }, the main one keyed
    /// "main"; X/Y/Width/Height are then not used. Omit it for one print area given by X/Y/Width/Height.
    /// </summary>
    public string? Areas { get; set; }

    /// <summary>Makes the photo another side (View: back, left or right) of this template, linked in the same save.</summary>
    public Guid? MainTemplateId { get; set; }
}

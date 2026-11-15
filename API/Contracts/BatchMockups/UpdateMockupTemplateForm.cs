using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.BatchMockups;

/// <summary>Multipart form used to update a personal mock-up template. <see cref="BaseImage"/> is optional.</summary>
public sealed class UpdateMockupTemplateForm
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

    /// <summary>Every print area as a JSON array, as on <see cref="CreateMockupTemplateForm.Areas"/>.</summary>
    public string? Areas { get; set; }
}

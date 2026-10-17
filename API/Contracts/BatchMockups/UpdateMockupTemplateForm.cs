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
}

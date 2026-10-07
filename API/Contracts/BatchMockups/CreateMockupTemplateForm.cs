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
}

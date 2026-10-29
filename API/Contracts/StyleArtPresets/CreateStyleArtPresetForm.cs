using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.StyleArtPresets;

/// <summary>Multipart form used to create a seller art style.</summary>
public sealed class CreateStyleArtPresetForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StyleModifiers { get; set; } = string.Empty;
    public string? RecommendationsJson { get; set; }
    public IFormFile? Preview { get; set; }
}

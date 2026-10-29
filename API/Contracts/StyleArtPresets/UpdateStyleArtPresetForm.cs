using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.StyleArtPresets;

/// <summary>Multipart form used to update a seller art style.</summary>
public sealed class UpdateStyleArtPresetForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StyleModifiers { get; set; } = string.Empty;
    public string? RecommendationsJson { get; set; }
    public IFormFile? Preview { get; set; }
    public bool DeletePreview { get; set; }
}

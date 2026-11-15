using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.AdminStyleArtPresets;

/// <summary>Multipart form used to create a system art style.</summary>
public sealed class CreateAdminStyleArtPresetForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StyleModifiers { get; set; } = string.Empty;
    public string? RecommendationsJson { get; set; }
    public IFormFile? Preview { get; set; }
}

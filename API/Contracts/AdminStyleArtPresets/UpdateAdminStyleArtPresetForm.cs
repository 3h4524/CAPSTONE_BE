using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.AdminStyleArtPresets;

/// <summary>Multipart form used to update a system art style.</summary>
public sealed class UpdateAdminStyleArtPresetForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StyleModifiers { get; set; } = string.Empty;
    public string? RecommendationsJson { get; set; }
    public IFormFile? Preview { get; set; }
    public bool DeletePreview { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ViName { get; set; }
    public string? ViDescription { get; set; }
    public string? ViRecommendationsJson { get; set; }
}

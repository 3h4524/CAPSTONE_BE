using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.MockupTemplates;

/// <summary>Multipart form used to create a seller mock-up template.</summary>
public sealed class CreateMockupTemplateForm
{
    public string Name { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string PrintAreaConfig { get; set; } = string.Empty;
    public int OutputWidthPx { get; set; }
    public int OutputHeightPx { get; set; }
    public IFormFile? BaseImage { get; set; }
    public IFormFile? Preview { get; set; }
}

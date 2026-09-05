using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a mockup backdrop that a design is composited onto.
/// </summary>
public sealed class MockupTemplate : AuditableEntity
{
    private readonly List<MockupImage> _mockupImages = [];

    private MockupTemplate()
    {
    }

    /// <summary>
    /// Gets the template name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the product type the mockup depicts.
    /// </summary>
    public string ProductType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the backdrop image URL.
    /// </summary>
    public string BaseImageUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the preview image URL.
    /// </summary>
    public string? PreviewImageUrl { get; private set; }

    /// <summary>
    /// Gets the print-area placement configuration as JSON.
    /// </summary>
    public string PrintAreaConfig { get; private set; } = "{}";

    /// <summary>
    /// Gets the rendered output width in pixels.
    /// </summary>
    public int OutputWidthPx { get; private set; }

    /// <summary>
    /// Gets the rendered output height in pixels.
    /// </summary>
    public int OutputHeightPx { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the template ships with the system.
    /// </summary>
    public bool IsSystemTemplate { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the template is selectable.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the number of times the template has been used.
    /// </summary>
    public int UsageCount { get; private set; }

    /// <summary>
    /// Gets the mockups rendered from this template.
    /// </summary>
    public IReadOnlyCollection<MockupImage> MockupImages => _mockupImages.AsReadOnly();
}

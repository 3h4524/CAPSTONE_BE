using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a reusable design prompt template.
/// </summary>
/// <remarks>
/// A template is either system-provided (no owner) or user-owned; the two are mutually exclusive.
/// </remarks>
public sealed class DesignTemplate : SoftDeletableEntity
{
    private readonly List<Product> _products = [];

    private DesignTemplate()
    {
    }

    /// <summary>
    /// Gets the owning user identifier, or <see langword="null"/> for system templates.
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Gets the template name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the template type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the target niche category.
    /// </summary>
    public string? NicheCategory { get; private set; }

    /// <summary>
    /// Gets the art style.
    /// </summary>
    public string? ArtStyle { get; private set; }

    /// <summary>
    /// Gets the base prompt.
    /// </summary>
    public string BasePrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets few-shot example prompts as JSON.
    /// </summary>
    public string ExamplePrompts { get; private set; } = "[]";

    /// <summary>
    /// Gets the style description.
    /// </summary>
    public string? StyleDescription { get; private set; }

    /// <summary>
    /// Gets the preview image URL.
    /// </summary>
    public string? PreviewImageUrl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the template ships with the system.
    /// </summary>
    public bool IsSystemTemplate { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the template is selectable.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the number of times the template has been used.
    /// </summary>
    public int UsageCount { get; private set; }

    /// <summary>
    /// Gets the products created from this template.
    /// </summary>
    public IReadOnlyCollection<Product> Products => _products.AsReadOnly();
}

using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a reusable prompt template for design generation.
/// </summary>
public sealed class DesignTemplate : SoftDeletableEntity
{
    private readonly List<AiPrompt> _aiPrompts = [];

    private DesignTemplate()
    {
    }

    /// <summary>
    /// Gets the owning seller identifier, or <see langword="null"/> for a system template.
    /// </summary>
    public int? SellerId { get; private set; }

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
    /// Gets the visual art style.
    /// </summary>
    public string? ArtStyle { get; private set; }

    /// <summary>
    /// Gets the base generation prompt.
    /// </summary>
    public string BasePrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets example prompts as JSON.
    /// </summary>
    public string ExamplePrompts { get; private set; } = "[]";

    /// <summary>
    /// Gets the human-readable style description.
    /// </summary>
    public string? StyleDescription { get; private set; }

    /// <summary>
    /// Gets the preview image URL.
    /// </summary>
    public string? PreviewImageUrl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the template is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the number of times the template has been used.
    /// </summary>
    public int UsageCount { get; private set; }

    /// <summary>
    /// Gets the prompts generated from this template.
    /// </summary>
    public IReadOnlyCollection<AiPrompt> AiPrompts => _aiPrompts.AsReadOnly();
}

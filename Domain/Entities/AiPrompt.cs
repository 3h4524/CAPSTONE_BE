using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a versioned AI prompt generated for a product.
/// </summary>
public sealed class AiPrompt : AuditableEntity
{
    private readonly List<DesignImage> _designImages = [];

    private AiPrompt()
    {
    }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source design template identifier, when applicable.
    /// </summary>
    public int? DesignTemplateId { get; private set; }

    /// <summary>
    /// Gets the original user description.
    /// </summary>
    public string OriginalDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the system prompt used for generation.
    /// </summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets few-shot examples as JSON.
    /// </summary>
    public string? FewShotExamples { get; private set; }

    /// <summary>
    /// Gets the final generated prompt.
    /// </summary>
    public string GeneratedPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the prompt version number.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets a value indicating whether the seller approved the prompt.
    /// </summary>
    public bool IsApprovedByUser { get; private set; }

    /// <summary>
    /// Gets optional seller notes.
    /// </summary>
    public string? UserNotes { get; private set; }

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source design template, when applicable.
    /// </summary>
    public DesignTemplate? DesignTemplate { get; private set; }

    /// <summary>
    /// Gets the images generated from this prompt.
    /// </summary>
    public IReadOnlyCollection<DesignImage> DesignImages => _designImages.AsReadOnly();
}

using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a generated image prompt, versioned per product.
/// </summary>
public sealed class AiPrompt : AuditableEntity
{
    private readonly List<DesignImage> _designImages = [];

    private AiPrompt()
    {
    }

    /// <summary>
    /// Gets the owning product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the source design template identifier, when one was used.
    /// </summary>
    public Guid? DesignTemplateId { get; private set; }

    /// <summary>
    /// Gets the seller-provided description the prompt was built from.
    /// </summary>
    public string OriginalDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the system prompt supplied to the model.
    /// </summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the few-shot examples as JSON.
    /// </summary>
    public string? FewShotExamples { get; private set; }

    /// <summary>
    /// Gets the final generated prompt.
    /// </summary>
    public string GeneratedPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the prompt version within the product.
    /// </summary>
    public int VersionNumber { get; private set; } = 1;

    /// <summary>
    /// Gets a value indicating whether the seller approved the prompt.
    /// </summary>
    public bool IsApprovedByUser { get; private set; }

    /// <summary>
    /// Gets the seller's notes on the prompt.
    /// </summary>
    public string? UserNotes { get; private set; }

    /// <summary>
    /// Gets the owning product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source design template, when one was used.
    /// </summary>
    public DesignTemplate? DesignTemplate { get; private set; }

    /// <summary>
    /// Gets the images generated from this prompt.
    /// </summary>
    public IReadOnlyCollection<DesignImage> DesignImages => _designImages.AsReadOnly();
}

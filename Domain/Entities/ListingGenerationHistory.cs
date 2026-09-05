using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records one listing generation attempt and what the seller did with the result.
/// </summary>
public sealed class ListingGenerationHistory : CreationTrackedEntity
{
    private ListingGenerationHistory()
    {
    }

    /// <summary>
    /// Gets the owning listing content identifier.
    /// </summary>
    public Guid ListingContentId { get; private set; }

    /// <summary>
    /// Gets the provider call behind this attempt, when recorded.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the attempt number within the listing.
    /// </summary>
    public int GenerationNumber { get; private set; }

    /// <summary>
    /// Gets the prompt sent to the model.
    /// </summary>
    public string PromptUsed { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the seller's adjustment hint for a regeneration.
    /// </summary>
    public string? AdjustmentHint { get; private set; }

    /// <summary>
    /// Gets the raw model response.
    /// </summary>
    public string RawAiResponse { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the title produced by this attempt.
    /// </summary>
    public string? TitleGenerated { get; private set; }

    /// <summary>
    /// Gets the tags produced by this attempt.
    /// </summary>
    public string[]? TagsGenerated { get; private set; }

    /// <summary>
    /// Gets the description produced by this attempt.
    /// </summary>
    public string? DescriptionGenerated { get; private set; }

    /// <summary>
    /// Gets what the seller did with the result.
    /// </summary>
    public string UserAction { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the seller's feedback notes.
    /// </summary>
    public string? FeedbackNotes { get; private set; }

    /// <summary>
    /// Gets the owning listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

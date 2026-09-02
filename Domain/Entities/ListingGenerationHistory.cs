using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one historical listing-generation attempt.
/// </summary>
public sealed class ListingGenerationHistory : CreationTrackedEntity
{
    private ListingGenerationHistory()
    {
    }

    /// <summary>
    /// Gets the parent listing content identifier.
    /// </summary>
    public int ListingContentId { get; private set; }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the generation sequence number.
    /// </summary>
    public int GenerationNumber { get; private set; }

    /// <summary>
    /// Gets the prompt used for generation.
    /// </summary>
    public string PromptUsed { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the raw AI response.
    /// </summary>
    public string RawAiResponse { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generated title, when present.
    /// </summary>
    public string? TitleGenerated { get; private set; }

    /// <summary>
    /// Gets the generated tags, when present.
    /// </summary>
    public string[]? TagsGenerated { get; private set; }

    /// <summary>
    /// Gets the generated description, when present.
    /// </summary>
    public string? DescriptionGenerated { get; private set; }

    /// <summary>
    /// Gets the API response duration in milliseconds.
    /// </summary>
    public int ApiResponseTimeMs { get; private set; }

    /// <summary>
    /// Gets the number of API tokens used.
    /// </summary>
    public int ApiTokensUsed { get; private set; }

    /// <summary>
    /// Gets the API cost in USD.
    /// </summary>
    public decimal ApiCostUsd { get; private set; }

    /// <summary>
    /// Gets the seller action following generation.
    /// </summary>
    public string UserAction { get; private set; } = string.Empty;

    /// <summary>
    /// Gets optional feedback notes.
    /// </summary>
    public string? FeedbackNotes { get; private set; }

    /// <summary>
    /// Gets the parent listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}

namespace APCS.Application.Features.Rag.Dtos;

/// <summary>Server-side scope used to retrieve verified lessons for one owner's AI video scene.</summary>
public sealed record VideoExperienceSearchRequest(Guid OwnerId, string? TemplateCode = null,
    string? ModelKey = null, string? PrimaryAction = null);

/// <summary>Represents the business state of a private video lesson.</summary>
public sealed record RagVideoExperienceDto(Guid DocumentId, string Status, string? Version, string Content);

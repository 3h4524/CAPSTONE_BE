namespace APCS.Application.Features.DesignTemplates.Dtos.Request;

/// <summary>Defines product context used to resolve a stored template prompt.</summary>
public sealed record ResolveDesignTemplatePromptRequestDto(
    Guid TemplateId,
    string Subject,
    string Niche,
    string? ArtStyleOverride,
    IReadOnlyList<string> Keywords);

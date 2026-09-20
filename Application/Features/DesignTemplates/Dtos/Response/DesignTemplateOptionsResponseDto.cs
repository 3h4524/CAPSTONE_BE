namespace APCS.Application.Features.DesignTemplates.Dtos.Response;

/// <summary>Represents one selectable design-template option.</summary>
public sealed record DesignTemplateOptionResponseDto(string Value, string Label);

/// <summary>Represents metadata required by the design-template editor.</summary>
public sealed record DesignTemplateOptionsResponseDto(
    IReadOnlyList<DesignTemplateOptionResponseDto> Niches,
    IReadOnlyList<DesignTemplateOptionResponseDto> ArtStyles,
    IReadOnlyList<string> Placeholders,
    int MaximumBasePromptLength,
    int MaximumExamples,
    bool PreviewGenerationEnabled);

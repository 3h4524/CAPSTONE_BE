using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Request;

/// <summary>Defines fields shared by design-template create and update requests.</summary>
public interface IDesignTemplateUpsertRequest
{
    string Name { get; }
    string NicheCategory { get; }
    string ArtStyle { get; }
    string BasePrompt { get; }
    string? NegativePrompt { get; }
    IReadOnlyList<DesignTemplateExampleDto> Examples { get; }
}

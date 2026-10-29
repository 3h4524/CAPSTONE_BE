using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Request;

/// <summary>Defines design-template library filtering and pagination.</summary>
public sealed record ListDesignTemplatesRequestDto(
    int PageNumber = 1,
    int PageSize = DesignTemplateRules.DefaultPageSize,
    string? Search = null,
    string? NicheCategory = null,
    string? ArtStyle = null,
    string Scope = DesignTemplateScopes.System);

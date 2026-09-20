using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.DesignTemplates.Validators;

/// <summary>Validates design-template library filters.</summary>
public sealed class ListDesignTemplatesValidator : AbstractValidator<ListDesignTemplatesRequestDto>
{
    public ListDesignTemplatesValidator()
    {
        RuleFor(request => request.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, DesignTemplateRules.MaximumPageSize);
        RuleFor(request => request.NicheCategory)
            .Must(niche => niche is null || DesignTemplateNiches.All.Contains(niche));
        RuleFor(request => request.ArtStyle)
            .Must(style => style is null || DesignTemplateArtStyles.All.Contains(style));
        RuleFor(request => request.Scope).Must(DesignTemplateScopes.Values.Contains);
    }
}

using APCS.Application.Features.DesignTemplates.Dtos.Request;

namespace APCS.Application.Features.DesignTemplates.Validators;

/// <summary>Validates personal design-template creation.</summary>
public sealed class CreateDesignTemplateValidator : DesignTemplateUpsertValidator<CreateDesignTemplateRequestDto>;

using APCS.Application.Features.DesignTemplates.Dtos.Request;

namespace APCS.Application.Features.DesignTemplates.Validators;

/// <summary>Validates personal design-template updates.</summary>
public sealed class UpdateDesignTemplateValidator : DesignTemplateUpsertValidator<UpdateDesignTemplateRequestDto>;

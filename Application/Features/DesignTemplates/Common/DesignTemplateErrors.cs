using APCS.Common.Models;

namespace APCS.Application.Features.DesignTemplates.Common;

/// <summary>Creates expected errors returned by design-template use cases.</summary>
public static class DesignTemplateErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized("design_templates.unauthorized", "The request is not authenticated.");

    public static Error NotFound() =>
        Error.NotFound("design_templates.not_found", "The design template was not found.");

    public static Error SystemTemplateReadOnly() =>
        Error.Forbidden(
            "design_templates.system_read_only",
            "System templates are read-only. Clone the template to customize it.");

    public static Error DuplicateName() =>
        Error.Conflict(
            "design_templates.duplicate_name",
            "A personal template with this name already exists.");

    public static Error ActiveBatchReference() =>
        Error.Conflict(
            "design_templates.active_batch_reference",
            "This template is used by a batch job that has not finished.");

    public static Error CloneRequiresSystemTemplate() =>
        Error.Validation("Only an active system template can be cloned.");

    public static Error CloneNameUnavailable() =>
        Error.Conflict(
            "design_templates.clone_name_unavailable",
            "A unique name for the cloned template could not be created. Please try again.");

    public static Error PromptUnresolved() =>
        Error.Validation("The resolved prompt contains an unsupported or unresolved placeholder.");
}

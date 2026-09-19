using APCS.Common.Models;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Creates expected errors returned by batch mock-up use cases.</summary>
public static class MockupErrors
{
    public static Error Unauthenticated(string action) =>
        Error.Unauthorized("BatchMockups.Unauthenticated", $"Please sign in to {action} mock-up templates.");

    public static Error BatchNotFound() =>
        Error.NotFound("BatchMockups.BatchNotFound", "The batch was not found.");

    public static Error TemplateNotFound() =>
        Error.NotFound("BatchMockups.TemplateNotFound", "One or more mock-up templates were not found.");

    public static Error NotDraft() =>
        Error.Conflict("BatchMockups.NotDraft", "Mock-up templates can only be changed while the batch is a draft.");

    public static Error IncompatibleTemplate(string name) =>
        Error.Conflict("BatchMockups.IncompatibleTemplate", $"The template {name} does not match any product type in this batch.");

    public static Error DuplicateName() =>
        Error.Conflict("BatchMockups.DuplicateName", "A mock-up template with this name already exists.");

    public static Error UpdateNotOwner() =>
        Error.Forbidden("BatchMockups.NotOwner", "Only the creator can update this mock-up template.");

    public static Error DeleteNotOwner() =>
        Error.Forbidden("BatchMockups.NotOwner", "System mock-up templates cannot be deleted. Only the creator can delete this mock-up template.");

    public static Error TemplateInUse() =>
        Error.Conflict("BatchMockups.TemplateInUse", "This mock-up template is already used and cannot be deleted.");
}

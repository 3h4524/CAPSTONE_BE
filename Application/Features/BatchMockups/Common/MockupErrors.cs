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

    public static Error JobGenerating() =>
        Error.Conflict("BatchMockups.JobGenerating", "Mock-up templates can't be changed while the job is generating images. Try again when it finishes.");

    public static Error IncompatibleTemplate(string name) =>
        Error.Conflict("BatchMockups.IncompatibleTemplate", $"The template {name} does not match any product type in this batch.");

    public static Error TemplateNotFoundSingle() =>
        Error.NotFound("BatchMockups.TemplateNotFound", "This mock-up template was not found.");

    public static Error DuplicateName() =>
        Error.Conflict("BatchMockups.DuplicateName", "A mock-up template with this name already exists.");

    public static Error UpdateNotOwner() =>
        Error.Forbidden("BatchMockups.NotOwner", "You can only edit your own mock-up templates.");

    public static Error DeleteNotOwner() =>
        Error.Forbidden("BatchMockups.NotOwner", "You can only delete your own mock-up templates.");

    public static Error InvalidPrintArea() =>
        Error.Validation("The print area must fit inside the uploaded image.");

    public static Error DesignImageNotFound() =>
        Error.NotFound("BatchMockups.DesignImageNotFound", "The generated design image was not found.");

    public static Error TemplateNotUsable(string name) =>
        Error.Conflict("BatchMockups.TemplateNotUsable", $"The template {name} does not have a usable base photo yet. Choose another template or ask its owner to re-upload the photo.");

    public static Error JobNotReady() =>
        Error.Conflict("BatchMockups.JobNotReady", "Mock-ups can only be generated after the batch job has produced images.");

    public static Error ProcessingUnavailable() =>
        Error.Conflict("BatchMockups.ProcessingUnavailable", "Mock-up photos can't be processed right now: the background-removal model is not installed on the server. Restart the API so it downloads the model, then try again.");

    public static Error InvalidPreviewPhoto() =>
        Error.Validation("Choose an image file of 10 MB or smaller.");

    public static Error NotRecolorable(string reason) =>
        Error.Conflict("BatchMockups.NotRecolorable", $"This photo can't be recolored: {reason} Use a white or light-gray garment on a plain background.");

    public static Error RecolorNotAllowed(string name) =>
        Error.Conflict("BatchMockups.RecolorNotAllowed", $"The template {name} does not support garment colors.");

    public static Error ApprovalPending() =>
        Error.Conflict("BatchMockups.ApprovalPending", "Mock-ups are made after the designs are approved.");

    public static Error NoTemplatesSelected() =>
        Error.Conflict("BatchMockups.NoTemplatesSelected", "Select at least one mock-up template for this batch before generating.");
}

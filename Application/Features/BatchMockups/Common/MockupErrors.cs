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

    public static Error BackgroundNotAllowed(string name) =>
        Error.Conflict("BatchMockups.BackgroundNotAllowed", $"The background of the template {name} can't be changed: its garment could not be separated from the photo's background.");

    public static Error RecolorNotAllowed(string name) =>
        Error.Conflict("BatchMockups.RecolorNotAllowed", $"The template {name} does not support garment colors.");

    public static Error ApprovalPending() =>
        Error.Conflict("BatchMockups.ApprovalPending", "Mock-ups are made after the designs are approved.");

    public static Error SlotNotFound() =>
        Error.NotFound("BatchMockups.SlotNotFound", "This mock-up is not one the job makes any more. Reload its mock-ups and try again.");

    public static Error TemplateNotForProduct(string name) =>
        Error.Conflict("BatchMockups.TemplateNotForProduct", $"The template {name} is for another type of product than this one.");

    public static Error ModelPhotoNotFound() =>
        Error.NotFound("BatchMockups.ModelPhotoNotFound", "This model photo was not found.");

    public static Error ModelPhotoNotOwner() =>
        Error.Forbidden("BatchMockups.NotOwner", "You can only change your own model photos.");

    public static Error ModelPhotoRequired() =>
        Error.Validation("Upload a photo of a person wearing a plain, light garment.");

    public static Error GarmentSelectionRequired() =>
        Error.Validation("Select the garment on the photo.");

    public static Error GarmentNotFound() =>
        Error.Conflict("BatchMockups.GarmentNotFound", "The garment could not be found on this photo automatically. Select it by hand, or use a photo of a person facing the camera in a plain, light top.");

    public static Error ModelPhotoUnusable(string reason) =>
        Error.Conflict("BatchMockups.ModelPhotoUnusable", $"This model photo can't be used: {reason}");

    public static Error UnknownPrintArea() =>
        Error.Validation("A print area was not found on the template. Reopen the template and try again.");

    public static Error AreaDesignNotFound() =>
        Error.NotFound("BatchMockups.AreaDesignNotFound", "A design chosen for a print area was not found. Choose another design.");

    public static Error NothingToPrint() =>
        Error.Validation("Print a design in at least one print area.");

    public static Error UnknownView() =>
        Error.Validation("A template's other photos show its back, left sleeve or right sleeve.");

    public static Error ViewTaken() =>
        Error.Conflict("BatchMockups.ViewTaken", "This photo already belongs to another template or side. Upload it again for this side.");

    public static Error NotFrontView() =>
        Error.Validation("Choose a mock-up of the front of the garment: only the front is put on a model photo.");

    public static Error NoTemplatesSelected() =>
        Error.Conflict("BatchMockups.NoTemplatesSelected", "Select at least one mock-up template for this batch before generating.");

    public static Error OutsidePrintifyArea(string name) =>
        Error.Validation($"Keep the design inside the Printify print area of {name}: Printify prints nothing outside it.");

    public static Error PrintifyPhotoLocked() =>
        Error.Conflict("BatchMockups.PrintifyPhotoLocked",
            "The photos and print area of a Printify template come from Printify. Use its Printify settings to change them.");

    public static Error PrintifyColorNotOffered(string name, string color) =>
        Error.Validation($"{color} is not a Printify color of {name}. Choose one of its Printify colors.");
}

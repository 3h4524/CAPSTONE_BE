using APCS.Common.Models;

namespace APCS.Application.Features.BatchMockups.Printify;

public static class PrintifyTemplateErrors
{
    public static Error NoToken() =>
        Error.Conflict("PrintifyTemplates.NoToken", "Set Printify__CatalogToken on the server to use Printify in admin.");

    public static Error NoStore() =>
        Error.Conflict("PrintifyTemplates.NoStore", "The Printify account of Printify__CatalogToken has no store. Add one in Printify first.");

    public static Error Printify(string? reason) =>
        Error.Conflict("PrintifyTemplates.Printify", reason is null ? "Printify could not be reached. Try again in a minute." : $"Printify refused: {reason}");

    public static Error JobNotFound() =>
        Error.NotFound("PrintifyTemplates.JobNotFound", "This measuring job is over or was never started. Start it again.");

    public static Error JobNotDone() =>
        Error.Conflict("PrintifyTemplates.JobNotDone", "The measuring job has not finished yet.");

    public static Error UnknownView(string view) =>
        Error.Validation($"{view} is not a side of a garment. Choose front, back, left or right.");

    public static Error FrontRequired() =>
        Error.Validation("A template needs the front of the garment.");

    public static Error FrontIsTheTemplate() =>
        Error.Validation("The front is the template itself. Make a new template to change it.");

    public static Error NothingToMeasure() =>
        Error.Validation("Choose a side or a color to measure.");

    public static Error TooManyColors(int maximum) =>
        Error.Validation($"Measure at most {maximum} colors at a time.");

    public static Error ViewNotPrinted(string view, string provider) =>
        Error.Validation($"{provider} prints nothing on the {view} of this product.");

    public static Error ColorNotMade(string color) =>
        Error.Validation($"{color} is not a color this provider makes.");

    public static Error NoWhite() =>
        Error.Validation("This provider makes no White, which colors are measured against. Choose another provider.");

    public static Error CameraNotMeasured(string view) =>
        Error.Validation($"Choose one of the measured photos for the {view}.");

    public static Error NameRequired() =>
        Error.Validation("Give the template a name and a product type.");

    public static Error TemplateNotLinked() =>
        Error.NotFound("PrintifyTemplates.NotLinked", "This template is not a shared template linked to Printify.");

    public static Error InvalidHex() =>
        Error.Validation("Write the color as #RRGGBB.");

    public static Error HexTaken(string hex) =>
        Error.Conflict("PrintifyTemplates.HexTaken", $"{hex} is already another color of this blueprint. Change that one, or pick another color.");

    public static Error ColorRowNotFound() =>
        Error.NotFound("PrintifyTemplates.ColorNotFound", "This color is not in the list any more.");
}

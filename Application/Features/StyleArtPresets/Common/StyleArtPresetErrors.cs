using APCS.Common.Models;

namespace APCS.Application.Features.StyleArtPresets.Common;

/// <summary>Creates expected errors returned by art style use cases.</summary>
public static class StyleArtPresetErrors
{
    public static Error Unauthenticated(string action) =>
        Error.Unauthorized("StyleArtPresets.Unauthenticated", $"Please sign in to {action} art styles.");

    public static Error NotFound() =>
        Error.NotFound("StyleArtPresets.NotFound", "The art style was not found.");

    public static Error DuplicateName() =>
        Error.Conflict("StyleArtPresets.DuplicateName", "An art style with this name already exists.");

    public static Error UpdateNotOwner() =>
        Error.Forbidden("StyleArtPresets.NotOwner", "Only the creator can update this art style.");

    public static Error DeleteNotOwner() =>
        Error.Forbidden("StyleArtPresets.NotOwner", "System art styles cannot be deleted. Only the creator can delete this art style.");
}

namespace APCS.Application.Features.BatchMockups;

/// <summary>How model photos are used. Bound to <c>ModelPhotos</c>.</summary>
public sealed class ModelPhotoSettings
{
    public const string SectionName = "ModelPhotos";

    /// <summary>
    /// Whether a mock-up can be put on a model photo without generative AI. That needs the garment found on the photo
    /// and print maps made from it, which is most of the time a photo takes to save. Off, a photo is only stored and
    /// mock-ups are put on it with AI; its code is kept, and a photo saved meanwhile gets its maps by finding its
    /// garment again once this is back on.
    /// </summary>
    public bool PrintWithoutAi { get; set; }
}

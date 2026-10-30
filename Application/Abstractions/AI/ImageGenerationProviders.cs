namespace APCS.Application.Abstractions.AI;

/// <summary>The image generation providers a Seller can choose for design images, by the name their API key is stored under.</summary>
public static class ImageGenerationProviders
{
    public const string Gemini = "gemini";
    public const string Ideogram = "ideogram";

    /// <summary>The provider used when a job does not name one, as every job did before Ideogram was added.</summary>
    public const string Default = Gemini;

    public static IReadOnlyList<string> All { get; } = [Gemini, Ideogram];

    /// <summary>The name shown to the Seller, e.g. in a missing-key message.</summary>
    public static string Label(string provider) => provider switch
    {
        Ideogram => "Ideogram",
        _ => "Gemini"
    };
}

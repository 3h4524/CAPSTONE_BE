namespace APCS.Application.Abstractions.Translation;

/// <summary>
/// Translates a batch of source strings into one target locale.
/// </summary>
public interface ITranslationProvider
{
    string Name { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyCollection<TranslationInput> inputs,
        string sourceLocale,
        string targetLocale,
        CancellationToken cancellationToken = default);
}

public sealed record TranslationInput(string SourceHash, string SourceText);

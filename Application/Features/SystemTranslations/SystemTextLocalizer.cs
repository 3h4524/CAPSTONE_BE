using System.Diagnostics.CodeAnalysis;

namespace APCS.Application.Features.SystemTranslations;

public sealed class SystemTextLocalizer
{
    public static readonly SystemTextLocalizer English = new(null);

    private readonly SystemTranslationDocument.Document? document;

    private SystemTextLocalizer(SystemTranslationDocument.Document? document) => this.document = document;

    public static SystemTextLocalizer For(string? translations, string culture) =>
        culture == SystemTranslationDocument.Vietnamese && SystemTranslationDocument.Parse(translations) is { } document
            ? new SystemTextLocalizer(document)
            : English;

    [return: NotNullIfNotNull(nameof(english))]
    public string? Text(string field, string? english) =>
        english is not null
        && document is not null
        && document.Source.TryGetValue(field, out var source)
        && source == english
        && document.Vi.TryGetValue(field, out var vi)
        && vi.Length > 0
            ? vi
            : english;
}

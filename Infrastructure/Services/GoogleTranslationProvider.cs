using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.Translation;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Uses Google Cloud Translation Basic's REST endpoint for dynamic content.
/// </summary>
public sealed class GoogleTranslationProvider(
    HttpClient httpClient,
    IOptions<TranslationOptions> options,
    ILogger<GoogleTranslationProvider> logger) : ITranslationProvider
{
    private readonly TranslationOptions _options = options.Value;

    public string Name => "google";

    public bool IsConfigured =>
        _options.Provider.Equals(Name, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.Endpoint);

    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyCollection<TranslationInput> inputs,
        string sourceLocale,
        string targetLocale,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || inputs.Count == 0)
            return new Dictionary<string, string>();

        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"{_options.Endpoint}?key={Uri.EscapeDataString(_options.ApiKey)}",
                new
                {
                    q = inputs.Select(input => input.SourceText).ToArray(),
                    source = sourceLocale,
                    target = targetLocale,
                    format = "text"
                },
                cancellationToken);

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var translations = document.RootElement
                .GetProperty("data")
                .GetProperty("translations")
                .EnumerateArray()
                .ToArray();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var inputArray = inputs.ToArray();
            for (var index = 0; index < Math.Min(inputArray.Length, translations.Length); index++)
            {
                var translatedText = translations[index].GetProperty("translatedText").GetString();
                if (!string.IsNullOrWhiteSpace(translatedText))
                    result[inputArray[index].SourceHash] = translatedText;
            }

            return result;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Translation provider request failed; returning source content.");
            return new Dictionary<string, string>();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Translation provider returned an unexpected response; returning source content.");
            return new Dictionary<string, string>();
        }
    }
}

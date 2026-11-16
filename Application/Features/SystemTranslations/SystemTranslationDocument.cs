using System.Globalization;
using System.Text.Json;

namespace APCS.Application.Features.SystemTranslations;

/// <summary>
/// The Vietnamese machine translation stored on a system catalog row (<c>translations</c> jsonb). The document keeps the
/// English text each field was translated from, so a translation of text that has since changed is never shown and the
/// background worker knows to translate the row again.
/// </summary>
public static class SystemTranslationDocument
{
    public const string Vietnamese = "vi";
    public const string Pending = "pending";
    public const string Translated = "ok";
    public const string Failed = "failed";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];
    private static readonly TimeSpan FailedRetryDelay = TimeSpan.FromHours(6);

    public sealed record Document(
        string Status,
        int Attempts,
        DateTime? NextRetryAt,
        IReadOnlyDictionary<string, string> Source,
        IReadOnlyDictionary<string, string> Vi);

    public static Document? Parse(string? translations)
    {
        if (string.IsNullOrWhiteSpace(translations))
            return null;

        try
        {
            using var json = JsonDocument.Parse(translations);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var status = root.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.String
                ? statusValue.GetString() ?? Pending
                : Pending;
            var attempts = root.TryGetProperty("attempts", out var attemptsValue)
                && attemptsValue.ValueKind == JsonValueKind.Number
                && attemptsValue.TryGetInt32(out var count)
                    ? count
                    : 0;
            DateTime? nextRetryAt = null;
            if (root.TryGetProperty("nextRetryAt", out var nextValue)
                && nextValue.ValueKind == JsonValueKind.String
                && DateTime.TryParse(nextValue.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                nextRetryAt = parsed;
            }

            return new Document(status, attempts, nextRetryAt, ReadFields(root, "en"), ReadFields(root, Vietnamese));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool NeedsTranslation(string? translations, IReadOnlyDictionary<string, string> fields, DateTime now)
    {
        if (fields.Count == 0)
            return false;

        var document = Parse(translations);
        if (document is null || !SameFields(document.Source, fields))
            return true;

        return document.Status != Translated && document.NextRetryAt is { } dueAt && dueAt <= now;
    }

    public static DateTime? NextRetryAt(string? translations) =>
        Parse(translations) is { Status: not Translated, NextRetryAt: { } dueAt } ? dueAt : null;

    public static string Succeeded(IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, string> vi) =>
        Serialize(Translated, 0, null, fields, vi);

    public static string AfterFailedAttempt(string? translations, IReadOnlyDictionary<string, string> fields, DateTime now)
    {
        var previous = Parse(translations);
        var attempts = (previous is not null && SameFields(previous.Source, fields) ? previous.Attempts : 0) + 1;
        var status = attempts > RetryDelays.Length ? Failed : Pending;
        var delay = attempts > RetryDelays.Length ? FailedRetryDelay : RetryDelays[attempts - 1];
        var stillValid = previous is null
            ? new Dictionary<string, string>()
            : previous.Vi
                .Where(pair => previous.Source.TryGetValue(pair.Key, out var source)
                    && fields.TryGetValue(pair.Key, out var current)
                    && source == current)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return Serialize(status, attempts, now + delay, fields, stillValid);
    }

    private static string Serialize(
        string status,
        int attempts,
        DateTime? nextRetryAt,
        IReadOnlyDictionary<string, string> source,
        IReadOnlyDictionary<string, string> vi)
    {
        var document = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["attempts"] = attempts
        };
        if (nextRetryAt.HasValue)
            document["nextRetryAt"] = nextRetryAt.Value.ToString("o", CultureInfo.InvariantCulture);
        document["en"] = new SortedDictionary<string, string>(source.ToDictionary(), StringComparer.Ordinal);
        document[Vietnamese] = new SortedDictionary<string, string>(vi.ToDictionary(), StringComparer.Ordinal);

        return JsonSerializer.Serialize(document);
    }

    private static bool SameFields(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static IReadOnlyDictionary<string, string> ReadFields(JsonElement root, string property)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            return fields;

        foreach (var field in value.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is { } text)
                fields[field.Name] = text;
        }

        return fields;
    }
}

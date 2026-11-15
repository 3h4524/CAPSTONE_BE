using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Moderation;

/// <summary>
/// The content guardrail: the blocklist first, then Gemini, then <see cref="ModerationPolicy"/>. Every evaluated
/// decision is recorded in <c>moderation_checks</c>, with the seller's text when it was not allowed; a text verdict is
/// cached by the hash of the normalized text, the blocklist and the settings, so an administrator's change applies at once.
/// A check that cannot run is <see cref="ModerationDecision.Unchecked"/>: an input caller only refuses a block, so the
/// text goes through (the blocklist has still run), while an output image stays unapprovable until it is checked.
/// </summary>
public sealed class ContentModerationService(
    ModerationBlocklistCache blocklist,
    ModerationRuntimeSettings runtimeSettings,
    IModerationStore store,
    IGeminiTextGenerationProvider gemini,
    ICacheService cache,
    IOptions<ModerationOptions> options,
    ILogger<ContentModerationService> logger) : IContentModerationService
{
    /// <summary>The longest part of the seller's text kept with a check, for an administrator to review.</summary>
    public const int MaximumExcerptLength = 500;

    private static readonly JsonSerializerOptions CategoriesJson = new(JsonSerializerDefaults.Web);
    private readonly ModerationOptions settings = options.Value;

    private bool ClassifierAvailable(ModerationSettingsSnapshot runtime) =>
        settings.Enabled && runtime.Enabled && gemini.IsConfigured;

    public async Task<ModerationVerdict> CheckTextAsync(
        IReadOnlyList<ModerationText> texts,
        ModerationContext context,
        CancellationToken cancellationToken = default,
        bool blocklistOnly = false)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(context);
        var fields = texts
            .Where(text => !string.IsNullOrWhiteSpace(text.Value))
            .Select(text => text with { Value = text.Value!.Trim() })
            .ToList();
        if (fields.Count == 0) return ModerationVerdict.Allowed(ModerationDeciders.Blocklist);

        var hash = Hash(Encoding.UTF8.GetBytes(string.Join('\n',
            fields.Select(field => $"{field.Field}\u001f{TextNormalizer.Normalize(field.Value)}"))));
        var stopwatch = Stopwatch.StartNew();
        var matcher = await blocklist.GetAsync(cancellationToken);
        var runtime = await runtimeSettings.GetAsync(cancellationToken);
        var cacheKey = $"moderation:v{settings.PolicyVersion}:{matcher.Fingerprint}:{runtime.Fingerprint}:text:{hash}";
        if (!blocklistOnly && await ReadCacheAsync(cacheKey, cancellationToken) is { } cached) return cached;

        var hits = fields
            .SelectMany(field => matcher.Match(field.Value).Select(hit => (field.Field, Hit: hit)))
            .ToList();

        ModerationVerdict verdict;
        string? model = null;
        var blocking = hits.Where(item => item.Hit.Blocks).ToList();
        if (blocking.Count > 0)
        {
            verdict = new ModerationVerdict(ModerationDecision.Block,
                blocking.Select(item => new ModerationFinding(item.Hit.Category, ModerationSeverity.High, item.Field, item.Hit.Term)).ToList(),
                ModerationDeciders.Blocklist);
        }
        else if (blocklistOnly || !ClassifierAvailable(runtime))
        {
            // Nothing to record: without the classifier there is no decision beyond "no blocking term".
            return ModerationVerdict.Allowed(ModerationDeciders.Disabled);
        }
        else
        {
            model = settings.Model;
            verdict = await ClassifyAsync(
                new GeminiTextGenerationRequest(GeminiModerationPrompt.TextPrompt(fields, hits.Select(item => item.Hit).ToList())),
                hits.Select(item => item.Hit.Category).Distinct().ToList(),
                runtime,
                cancellationToken);
        }

        stopwatch.Stop();
        await RecordAsync(context, hash, verdict, model, stopwatch, cancellationToken,
            verdict.Decision == ModerationDecision.Allow ? null : Excerpt(fields));
        if (verdict.Decision != ModerationDecision.Unchecked) await WriteCacheAsync(cacheKey, verdict, cancellationToken);
        // Allowed text is not logged: a flagged or blocked one is, so the classifier's ratings can be followed.
        if (verdict.Decision != ModerationDecision.Allow)
            logger.LogInformation("Moderation {Decision} for {Feature}/{Subject} by {Decider}: {Findings}",
                verdict.Decision, context.Feature, context.SubjectType, verdict.DecidedBy, Describe(verdict));
        return verdict;
    }

    public async Task<ModerationVerdict> CheckImageAsync(
        byte[] imageBytes,
        string mimeType,
        ModerationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(context);
        var runtime = await runtimeSettings.GetAsync(cancellationToken);
        if (!ClassifierAvailable(runtime)) return ModerationVerdict.Allowed(ModerationDeciders.Disabled);

        var stopwatch = Stopwatch.StartNew();
        var hash = Hash(imageBytes);
        ModerationVerdict verdict;
        if (imageBytes.Length == 0 || mimeType is not ("image/png" or "image/jpeg" or "image/webp"))
            verdict = ModerationVerdict.NotChecked($"The image type {mimeType} cannot be checked.");
        else
            verdict = await ClassifyAsync(
                new GeminiTextGenerationRequest(GeminiModerationPrompt.ImagePrompt,
                    Images: [new GeminiInputImage(imageBytes, mimeType)]),
                [],
                runtime,
                cancellationToken);
        stopwatch.Stop();

        await RecordAsync(context, hash, verdict, settings.Model, stopwatch, cancellationToken);
        if (verdict.Decision != ModerationDecision.Allow)
            logger.LogInformation("Moderation {Decision} for {Feature}/{Subject} {SubjectId} by {Decider}: {Findings}",
                verdict.Decision, context.Feature, context.SubjectType, context.SubjectId, verdict.DecidedBy, Describe(verdict));
        return verdict;
    }

    public async Task<ModerationDecision?> GetLatestOutputDecisionAsync(
        string subjectType,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await store.GetLatestOutputDecisionAsync(subjectType, subjectId, cancellationToken);
        return stored is null ? null : Enum.TryParse<ModerationDecision>(stored, true, out var decision) ? decision : null;
    }

    private async Task<ModerationVerdict> ClassifyAsync(
        GeminiTextGenerationRequest request,
        IReadOnlyList<string> hintedCategories,
        ModerationSettingsSnapshot runtime,
        CancellationToken cancellationToken)
    {
        // The provider retries an overloaded model; the whole check is still held to a small multiple of one attempt.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds * 2));
        try
        {
            var answer = await gemini.GenerateAsync(request with
            {
                SystemInstruction = GeminiModerationPrompt.SystemInstruction,
                Temperature = 0m,
                ResponseSchema = GeminiModerationPrompt.ResponseSchema(),
                Model = settings.Model,
                FallbackModel = settings.Model,
                AttemptTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
            }, timeout.Token);
            var (findings, reason) = GeminiModerationPrompt.Parse(answer.Text);
            return new ModerationVerdict(ModerationPolicy.Decide(findings, runtime), findings, ModerationDeciders.Gemini, reason);
        }
        catch (GeminiContentBlockedException blocked)
        {
            // Gemini's own filter would not even look at it: treated as the clearest possible refusal.
            var categories = hintedCategories.Count > 0 ? hintedCategories : [ModerationCategories.Sexual];
            return new ModerationVerdict(ModerationDecision.Block,
                categories.Select(category => new ModerationFinding(category, ModerationSeverity.High, Evidence: blocked.Reason)).ToList(),
                ModerationDeciders.ProviderSafety);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The moderation check did not answer within {Seconds} seconds", settings.TimeoutSeconds * 2);
            return ModerationVerdict.NotChecked("The content check did not answer in time.");
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or ArgumentException or JsonException)
        {
            logger.LogWarning(error, "The moderation check could not run");
            return ModerationVerdict.NotChecked("The content check is unavailable.");
        }
    }

    // "<field>: <value>" per line, cut to MaximumExcerptLength characters.
    internal static string Excerpt(IEnumerable<ModerationText> fields)
    {
        var text = string.Join('\n', fields.Select(field => $"{field.Field}: {field.Value}"));
        return text.Length <= MaximumExcerptLength ? text : text[..(MaximumExcerptLength - 1)] + "…";
    }

    private async Task RecordAsync(ModerationContext context, string hash, ModerationVerdict verdict, string? model,
        Stopwatch stopwatch, CancellationToken cancellationToken, string? excerpt = null)
    {
        try
        {
            await store.AddCheckAsync(new ModerationCheckRecord(
                context.UserId,
                context.Feature,
                context.Direction == ModerationDirection.Output ? "output" : "input",
                context.SubjectType,
                context.SubjectId,
                hash,
                verdict.Decision.ToString().ToLowerInvariant(),
                verdict.DecidedBy,
                JsonSerializer.Serialize(verdict.Findings.Select(finding => new
                {
                    finding.Category,
                    Severity = finding.Severity.ToString().ToLowerInvariant(),
                    finding.Field,
                    finding.Evidence
                }), CategoriesJson),
                verdict.Reason,
                verdict.DecidedBy is ModerationDeciders.Gemini or ModerationDeciders.ProviderSafety ? model : null,
                (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                settings.PolicyVersion,
                excerpt), cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The decision stands even when it cannot be written down.
            logger.LogWarning(error, "A moderation decision could not be recorded");
        }
    }

    private async Task<ModerationVerdict?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        if (settings.CacheMinutes <= 0) return null;
        try
        {
            return await cache.GetAsync<ModerationVerdict>(key, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogDebug(error, "The moderation cache could not be read");
            return null;
        }
    }

    private async Task WriteCacheAsync(string key, ModerationVerdict verdict, CancellationToken cancellationToken)
    {
        if (settings.CacheMinutes <= 0) return;
        try
        {
            await cache.SetAsync(key, verdict, TimeSpan.FromMinutes(settings.CacheMinutes), cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogDebug(error, "The moderation cache could not be written");
        }
    }

    // "sexual/medium in the product name", for the log line. The seller's words themselves are not logged.
    private static string Describe(ModerationVerdict verdict) =>
        verdict.Findings.Count == 0
            ? verdict.Reason ?? "no findings"
            : string.Join("; ", verdict.Findings.Select(finding =>
                $"{finding.Category}/{finding.Severity.ToString().ToLowerInvariant()}" +
                (finding.Field is null ? string.Empty : $" in {finding.Field}")));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

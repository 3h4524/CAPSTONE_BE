using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.AI;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.ImageQuality.Dtos.Response;

namespace APCS.Application.Features.ImageQuality.Common;

/// <summary>A saved design image, as far as the live stats need it.</summary>
public sealed record LiveImageRow(
    DateTime CreatedAtUtc,
    string Model,
    string ApprovalStatus,
    decimal GenerationTimeSeconds,
    string? GenerationMetadata,
    Guid? BatchJobId,
    Guid ProductId,
    decimal? CostUsd);

/// <summary>A job log event the live stats count.</summary>
public sealed record LiveLogRow(Guid BatchJobId, string EventType, DateTime CreatedAtUtc);

/// <summary>
/// Groups saved design images and failed attempts by image provider and prompt version. An image's provider comes from
/// its model and its version from its label, or from the day it was made; a log event knows only its job, so its
/// provider comes from the job's settings and its version from its day.
/// </summary>
public static class ImageQualityLiveCalculator
{
    /// <summary>An attempt that saved no image because the provider failed.</summary>
    public const string GenerationFailed = "generation_failed";

    /// <summary>An attempt the provider refused on safety or copyright grounds.</summary>
    public const string GenerationBlocked = "generation_blocked";

    /// <summary>An image the provider made that could not be stored.</summary>
    public const string UploadFailed = "upload_failed";

    /// <summary>A Seller asking for a product's designs again.</summary>
    public const string RegenerateRequested = "regenerate_requested";

    /// <summary>Every log event the live stats count.</summary>
    public static IReadOnlyList<string> EventTypes { get; } =
        [GenerationFailed, GenerationBlocked, UploadFailed, RegenerateRequested];

    private const string UnlabeledLabel = "Unlabeled (older code)";

    /// <summary>Works out one row per image provider and prompt version, oldest version first.</summary>
    public static IReadOnlyList<ImageQualityLiveVersionResponseDto> Calculate(
        IReadOnlyList<LiveImageRow> images,
        IReadOnlyList<LiveLogRow> logs,
        IReadOnlyDictionary<Guid, string> jobConfigs)
    {
        var buckets = new Dictionary<(string Provider, string Version), Bucket>();
        Bucket BucketFor(string provider, string version)
        {
            if (!buckets.TryGetValue((provider, version), out var bucket))
            {
                bucket = new Bucket();
                buckets[(provider, version)] = bucket;
            }

            return bucket;
        }

        foreach (var image in images)
        {
            var label = ReadPromptVersion(image.GenerationMetadata);
            var version = DesignPromptVersions.Resolve(label, image.CreatedAtUtc);
            var config = image.BatchJobId is { } jobId ? jobConfigs.GetValueOrDefault(jobId) : null;
            BucketFor(ProviderOfModel(image.Model), version).Add(image, label is not null, ReadRequireApproval(config) != false);
        }

        foreach (var log in logs)
        {
            var bucket = BucketFor(ReadProvider(jobConfigs.GetValueOrDefault(log.BatchJobId)), DesignPromptVersions.AtDate(log.CreatedAtUtc));
            bucket.Count(log.EventType);
        }

        return buckets
            .OrderBy(entry => DesignPromptVersions.OrderOf(entry.Key.Version))
            .ThenBy(entry => entry.Key.Version, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Provider, StringComparer.Ordinal)
            .Select(entry => entry.Value.ToResponse(entry.Key.Provider, entry.Key.Version))
            .ToList();
    }

    /// <summary>The provider a model belongs to.</summary>
    public static string ProviderOfModel(string model) =>
        model.StartsWith(ImageGenerationProviders.Ideogram, StringComparison.OrdinalIgnoreCase)
            ? ImageGenerationProviders.Ideogram
            : ImageGenerationProviders.Gemini;

    private static string? ReadPromptVersion(string? metadata) =>
        ReadJson(metadata)?["promptVersion"] is JsonValue value && value.TryGetValue<string>(out var label) ? label : null;

    private static bool? ReadRequireApproval(string? config) =>
        ReadJson(config)?["requireApproval"] is JsonValue value && value.TryGetValue<bool>(out var requireApproval)
            ? requireApproval
            : null;

    private static string ReadProvider(string? config) =>
        ReadJson(config)?["imageProvider"] is JsonValue value
        && value.TryGetValue<string>(out var provider)
        && ImageGenerationProviders.All.Contains(provider)
            ? provider
            : ImageGenerationProviders.Default;

    private static JsonNode? ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class Bucket
    {
        private readonly HashSet<Guid> _products = [];
        private readonly HashSet<Guid> _jobs = [];
        private readonly List<decimal> _seconds = [];
        private readonly List<decimal> _costs = [];
        private int _images;
        private int _labeled;
        private int _reviewed;
        private int _approved;
        private int _rejected;
        private int _failed;
        private int _blocked;
        private int _regenerated;
        private int _textChecked;
        private int _textOk;
        private readonly List<double> _characterErrorRates = [];

        public void Add(LiveImageRow image, bool isLabeled, bool isReviewed)
        {
            _images++;
            if (DesignTextCheck.FromMetadata(image.GenerationMetadata) is { } textCheck)
            {
                _textChecked++;
                if (textCheck.Ok) _textOk++;
                if (textCheck.CharacterErrorRate is { } rate) _characterErrorRates.Add(rate);
            }

            _products.Add(image.ProductId);
            if (image.BatchJobId is { } jobId)
            {
                _jobs.Add(jobId);
            }

            _seconds.Add(image.GenerationTimeSeconds);
            if (image.CostUsd is { } cost)
            {
                _costs.Add(cost);
            }

            if (isLabeled)
            {
                _labeled++;
            }

            if (isReviewed)
            {
                _reviewed++;
                if (image.ApprovalStatus == ApprovalStatuses.Approved) _approved++;
                if (image.ApprovalStatus == ApprovalStatuses.Rejected) _rejected++;
            }
        }

        public void Count(string eventType)
        {
            switch (eventType)
            {
                case GenerationFailed or UploadFailed:
                    _failed++;
                    break;
                case GenerationBlocked:
                    _blocked++;
                    break;
                case RegenerateRequested:
                    _regenerated++;
                    break;
            }
        }

        public ImageQualityLiveVersionResponseDto ToResponse(string provider, string version)
        {
            var known = DesignPromptVersions.All.FirstOrDefault(item => item.Key == version);
            var next = known is null
                ? null
                : DesignPromptVersions.All.SkipWhile(item => item.Key != version).Skip(1).FirstOrDefault();
            var (since, until, label) = version == DesignPromptVersions.Unlabeled
                ? ((DateTime?)DesignPromptVersions.LabelingStartedUtc, (DateTime?)null, UnlabeledLabel)
                : (known?.SinceUtc, next?.SinceUtc, known?.Label ?? version);

            var decided = _approved + _rejected;
            var attempts = _images + _failed + _blocked;

            return new ImageQualityLiveVersionResponseDto(
                Key: $"{provider}:{version}",
                Label: $"{ImageGenerationProviders.Label(provider)} · {label}",
                Provider: provider,
                SinceUtc: ToOffset(since),
                UntilUtc: ToOffset(until),
                Images: _images,
                LabeledImages: _labeled,
                Products: _products.Count,
                Jobs: _jobs.Count,
                ReviewedImages: _reviewed,
                ApprovedImages: _approved,
                RejectedImages: _rejected,
                ApprovalRate: decided > 0 ? (double)_approved / decided : null,
                RegenerateRequests: _regenerated,
                RegenerateRate: _products.Count > 0 ? (double)_regenerated / _products.Count : null,
                FailedAttempts: _failed,
                BlockedAttempts: _blocked,
                FailureRate: attempts > 0 ? (double)(_failed + _blocked) / attempts : null,
                AverageGenerationSeconds: _seconds.Count > 0 ? (double)_seconds.Average() : null,
                AverageCostUsd: _costs.Count > 0 ? _costs.Average() : null,
                TotalCostUsd: _costs.Sum(),
                TextCheckedImages: _textChecked,
                TextOkImages: _textOk,
                TextOkRate: _textChecked > 0 ? (double)_textOk / _textChecked : null,
                AverageCharacterErrorRate: _characterErrorRates.Count > 0 ? _characterErrorRates.Average() : null);
        }

        private static DateTimeOffset? ToOffset(DateTime? value) =>
            value is { } moment ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)) : null;
    }
}

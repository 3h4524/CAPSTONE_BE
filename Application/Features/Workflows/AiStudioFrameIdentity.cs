using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Versioned identity stored in the existing regions JSON, so stale frames can be rejected without changing the table schema.</summary>
public sealed record AiStudioFrameReferenceMetadata(string ContentHash);
public sealed record AiStudioFrameMetadata(string Fingerprint, Guid SourceMockupId, string SourceHash,
    long SourceRevision, string Layout, string AspectRatio, int PromptVersion, string? CustomPrompt = null,
    string? TemplateCode = null, IReadOnlyList<AiStudioFrameReferenceMetadata>? References = null);

public static class AiStudioFrameIdentity
{
    public const int PromptVersion = 9;
    public const int DetailCompositionPromptVersion = 10;
    public const string HangingWindow = "hanging_window";
    public const string FlatLay = "flat_lay";
    public const string None = "none";

    public static bool IsSupportedLayout(string? layout) => layout is HangingWindow or FlatLay or None;

    public static string NormalizeCustomPrompt(string? customPrompt) => customPrompt?.Trim() ?? "";
    public static string NormalizeTemplateCode(string? templateCode) => templateCode?.Trim().ToLowerInvariant() ?? "";

    public static int PromptVersionFor(string layout, string? customPrompt) =>
        layout == None && !string.IsNullOrWhiteSpace(customPrompt) ? DetailCompositionPromptVersion : PromptVersion;

    public static string Fingerprint(MockupImage source, string layout, string aspectRatio, string? customPrompt = null,
        string? templateCode = null, IReadOnlyList<AiStudioFrameReferenceMetadata>? references = null) =>
        WorkflowJson.Hash(WorkflowJson.Write(new
        {
            source.Id,
            source.ContentHash,
            source.MetadataRevision,
            layout,
            aspectRatio,
            customPrompt = NormalizeCustomPrompt(customPrompt),
            templateCode = NormalizeTemplateCode(templateCode),
            references = NormalizeReferences(references).Select(reference => new
            {
                reference.ContentHash
            }),
            promptVersion = PromptVersionFor(layout, customPrompt)
        }));

    public static IReadOnlyList<AiStudioFrameReferenceMetadata> NormalizeReferences(
        IReadOnlyList<AiStudioFrameReferenceMetadata>? references) =>
        references?.ToArray() ?? [];

    public static IReadOnlyList<string> NormalizeReferenceHashes(IReadOnlyList<string>? referenceHashes) =>
        referenceHashes?.ToArray() ?? [];

    private static string LegacyFingerprintV2(MockupImage source, string layout, string aspectRatio) =>
        WorkflowJson.Hash(WorkflowJson.Write(new
        {
            source.Id,
            source.ContentHash,
            source.MetadataRevision,
            layout,
            aspectRatio,
            promptVersion = 2
        }));

    public static AiStudioFrameMetadata? Read(string? regions)
    {
        if (string.IsNullOrWhiteSpace(regions)) return null;
        try
        {
            using var document = JsonDocument.Parse(regions);
            if (!document.RootElement.TryGetProperty("studioFrame", out var value)) return null;
            return value.Deserialize<AiStudioFrameMetadata>(WorkflowJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializeRegions(AiStudioFrameMetadata metadata) => WorkflowJson.Write(new
    {
        focalPoint = new Point(),
        product = (Region?)null,
        artwork = (Region?)null,
        detail = (Region?)null,
        studioFrame = metadata
    });

    public static bool IsCurrent(MockupImage frame, MockupImage source, string layout, string aspectRatio,
        string? customPrompt = null, string? templateCode = null, IReadOnlyList<string>? referenceHashes = null)
    {
        if (frame.SourceType != APCS.Application.Features.BatchMockups.Common.MockupSources.AiGenerated) return false;
        var metadata = Read(frame.Regions);
        if (metadata is null || metadata.SourceMockupId != source.Id || metadata.SourceHash != source.ContentHash ||
            metadata.SourceRevision != source.MetadataRevision || metadata.Layout != layout || metadata.AspectRatio != aspectRatio)
            return false;

        if (metadata.PromptVersion == 2 && string.IsNullOrWhiteSpace(customPrompt) && string.IsNullOrWhiteSpace(metadata.CustomPrompt) &&
            string.IsNullOrWhiteSpace(templateCode) && referenceHashes is not { Count: > 0 })
            return metadata.Fingerprint == LegacyFingerprintV2(source, layout, aspectRatio);

        var storedReferences = NormalizeReferences(metadata.References);
        var requestedReferences = NormalizeReferenceHashes(referenceHashes);
        var referencesMatch = storedReferences.Count == requestedReferences.Count &&
            storedReferences.Zip(requestedReferences).All(pair =>
                pair.First.ContentHash == pair.Second);
        return metadata.PromptVersion == PromptVersionFor(layout, customPrompt) &&
            NormalizeCustomPrompt(metadata.CustomPrompt) == NormalizeCustomPrompt(customPrompt) &&
            NormalizeTemplateCode(metadata.TemplateCode) == NormalizeTemplateCode(templateCode) && referencesMatch &&
            metadata.Fingerprint == Fingerprint(source, layout, aspectRatio, customPrompt, templateCode, storedReferences);
    }
}

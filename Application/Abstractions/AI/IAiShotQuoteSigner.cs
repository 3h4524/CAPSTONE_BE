namespace APCS.Application.Abstractions.AI;

using APCS.Application.Features.Workflows;

public sealed record AiShotQuoteClaims(Guid RunId, string StoryboardFingerprint, bool Regenerate,
    decimal EstimatedCostUsd, DateTimeOffset ExpiresAtUtc, int? SceneOrder = null,
    string? ConfigFingerprint = null, Storyboard? PromptSnapshot = null);

public interface IAiShotQuoteSigner
{
    string Sign(AiShotQuoteClaims claims);
    AiShotQuoteClaims? Read(string token);
}

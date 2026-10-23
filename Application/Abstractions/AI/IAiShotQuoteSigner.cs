namespace APCS.Application.Abstractions.AI;

public sealed record AiShotQuoteClaims(Guid RunId, string StoryboardFingerprint, bool Regenerate,
    decimal EstimatedCostUsd, DateTimeOffset ExpiresAtUtc, int? SceneOrder = null);

public interface IAiShotQuoteSigner
{
    string Sign(AiShotQuoteClaims claims);
    AiShotQuoteClaims? Read(string token);
}

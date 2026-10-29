using APCS.Application.Abstractions.AI;

namespace APCS.Application.Features.Workflows;

public sealed class AiVideoGenerationStrategyFactory(IEnumerable<IAiVideoGenerationStrategy> strategies)
    : IAiVideoGenerationStrategyFactory
{
    private readonly IReadOnlyList<IAiVideoGenerationStrategy> _strategies = strategies.ToArray();

    public IAiVideoGenerationStrategy Resolve(string providerKey, string modelKey) =>
        _strategies.SingleOrDefault(strategy =>
            string.Equals(strategy.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(strategy.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No AI video strategy is registered for '{providerKey}/{modelKey}'.");
}

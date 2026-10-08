using System.Text.Json;
using APCS.Common.Models;
using APCS.Application.Features.Workflows.Validators;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowGraphValidator(WorkflowCapabilityRegistry registry, GenerateVideoConfigValidator videoValidator)
{
    public Error? Validate(WorkflowDefinition definition)
    {
        if (definition.Version != 2 || definition.Nodes.Count != 6 || definition.Edges.Count != 5)
            return Error.Validation("MVP requires exactly Product Input → Apply Mockup → Mockup Approval → Generate Video → Review Video → Export ZIP.");
        if (definition.Nodes.Select(x => x.Id).Distinct().Count() != definition.Nodes.Count || definition.Nodes.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 100))
            return Error.Validation("Node IDs must be unique and non-empty.");
        var enabled = registry.Get().Nodes.Where(x => x.Enabled).Select(x => x.Type).ToHashSet();
        if (definition.Nodes.Any(x => !enabled.Contains(x.Type)))
            return new("UnsupportedNode", "This workflow contains a node without a backend executor.", ErrorType.Validation);
        for (var i = 0; i < WorkflowCapabilityRegistry.Pipeline.Length; i++)
        {
            var matching = definition.Nodes.Where(x => x.Type == WorkflowCapabilityRegistry.Pipeline[i]).ToArray();
            if (matching.Length != 1) return Error.Validation("Each MVP node must appear exactly once.");
            if (i == 0) continue;
            var previous = definition.Nodes.Single(x => x.Type == WorkflowCapabilityRegistry.Pipeline[i - 1]);
            if (definition.Edges.Count(e => e.Source == previous.Id && e.Target == matching[0].Id) != 1)
                return Error.Validation("Node connections must preserve both approval gates.");
        }
        try
        {
            var input = WorkflowJson.Config<ProductInputConfig>(definition.Nodes.Single(x => x.Type == "product-input"));
            if (input.BatchId == Guid.Empty || input.ProductId == Guid.Empty) return Error.Validation("Select a batch and one product.");
            var config = WorkflowJson.Config<GenerateVideoConfig>(definition.Nodes.Single(x => x.Type == "generate-video"));
            if (!registry.IsModeEnabled(config.Mode)) return new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation);
            var validation = videoValidator.Validate(config);
            if (!validation.IsValid) return Error.Validation(string.Join(" ", validation.Errors.Select(x => x.ErrorMessage)));
            var assets = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
            if (assets.MockupIds is { Count: > 8 } || assets.MockupIds?.Distinct().Count() != assets.MockupIds?.Count)
                return Error.Validation("Select at most eight distinct mockups.");
        }
        catch (JsonException) { return Error.Validation("Invalid node configuration."); }
        return null;
    }
}

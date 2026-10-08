using System.Text.Json;
using APCS.Common.Models;
using APCS.Application.Features.Workflows.Validators;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowGraphValidator(WorkflowCapabilityRegistry registry, GenerateVideoConfigValidator videoValidator)
{
    public Error? Validate(WorkflowDefinition definition)
    {
        if (definition.Version != 2 || definition.Edges.Count != definition.Nodes.Count - 1)
            return Error.Validation("A video run needs Product Input → Apply Mockup → Mockup Approval → Generate Video → Review Video → Export ZIP in one line. The design steps may come before Apply Mockup.");
        if (definition.Nodes.Select(x => x.Id).Distinct().Count() != definition.Nodes.Count || definition.Nodes.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 100))
            return Error.Validation("Node IDs must be unique and non-empty.");
        var enabled = registry.Get().Nodes.Where(x => x.Enabled).Select(x => x.Type).ToHashSet();
        if (definition.Nodes.Any(x => !enabled.Contains(x.Type)))
            return new("UnsupportedNode", "This workflow contains a node without a backend executor.", ErrorType.Validation);
        if (WorkflowCapabilityRegistry.Pipeline.Any(type => definition.Nodes.Count(x => x.Type == type) != 1))
            return Error.Validation("Each MVP node must appear exactly once.");
        if (WorkflowCapabilityRegistry.BatchSteps.Any(type => definition.Nodes.Count(x => x.Type == type) > 1))
            return Error.Validation("Each design step may appear only once.");
        // One line in run order, so neither approval gate can be bypassed and the design steps stay ahead of the mock-ups.
        var ordered = definition.Nodes.OrderBy(x => Array.IndexOf(WorkflowCapabilityRegistry.RunOrder, x.Type)).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (definition.Edges.Count(e => e.Source == ordered[i - 1].Id && e.Target == ordered[i].Id) != 1)
                return Error.Validation("Node connections must preserve both approval gates.");
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

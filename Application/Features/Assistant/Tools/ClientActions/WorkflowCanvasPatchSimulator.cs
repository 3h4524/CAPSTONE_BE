using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record CanvasOperationArgs(
    string? Op,
    string? Ref,
    string? Type,
    string? Label,
    string? After,
    string? NodeId,
    string? Source,
    string? Target,
    string? ConfigJson);

public sealed record NormalizedCanvasOperation(
    string Op,
    string? Ref,
    string? Type,
    string? Label,
    string? After,
    string? NodeId,
    string? Source,
    string? Target,
    JsonElement? Config);

public sealed record CanvasPatchSimulation(
    IReadOnlyList<NormalizedCanvasOperation> Operations,
    WorkflowDefinition Result,
    IReadOnlyList<string> Errors);

public sealed class WorkflowCanvasPatchSimulator
{
    private const int MaxLabelLength = 100;

    public CanvasPatchSimulation Simulate(
        AssistantCanvasDto canvas,
        IReadOnlyList<CanvasOperationArgs> operations,
        IReadOnlySet<string> allowedNodeTypes)
    {
        var errors = new List<string>();
        var normalized = new List<NormalizedCanvasOperation>();
        var nodes = canvas.Nodes.ToDictionary(
            node => node.Id,
            node => new SimNode(node.Type, node.Label, JsonNode.Parse(node.Config.GetRawText()) as JsonObject ?? new JsonObject()));
        var edges = canvas.Edges.Select(edge => (edge.Source, edge.Target)).ToList();

        if (operations.Count is 0 or > AssistantLimits.MaxCanvasOperations)
            errors.Add($"Provide between 1 and {AssistantLimits.MaxCanvasOperations} operations.");

        foreach (var operation in operations.Take(AssistantLimits.MaxCanvasOperations))
        {
            var error = Apply(operation, nodes, edges, allowedNodeTypes, out var applied);
            if (error is not null)
                errors.Add(error);
            else if (applied is not null)
                normalized.Add(applied);
        }

        var result = new WorkflowDefinition(
            2,
            nodes.Select(pair => new WorkflowNode(
                pair.Key,
                pair.Value.Type,
                pair.Value.Label,
                new Point(),
                JsonSerializer.SerializeToElement(pair.Value.Config))).ToArray(),
            edges.Select(edge => new WorkflowEdge($"{edge.Source}->{edge.Target}", edge.Source, edge.Target)).ToArray());
        return new CanvasPatchSimulation(normalized, result, errors);
    }

    private static string? Apply(
        CanvasOperationArgs operation,
        Dictionary<string, SimNode> nodes,
        List<(string Source, string Target)> edges,
        IReadOnlySet<string> allowedNodeTypes,
        out NormalizedCanvasOperation? applied)
    {
        applied = null;
        switch (operation.Op)
        {
            case "add_node":
                return AddNode(operation, nodes, allowedNodeTypes, out applied);
            case "remove_node":
                if (operation.NodeId is null || !nodes.Remove(operation.NodeId))
                    return $"remove_node: node '{operation.NodeId}' does not exist.";
                edges.RemoveAll(edge => edge.Source == operation.NodeId || edge.Target == operation.NodeId);
                applied = Normalize(operation, null);
                return null;
            case "connect":
                if (operation.Source is null || operation.Target is null || !nodes.ContainsKey(operation.Source) || !nodes.ContainsKey(operation.Target))
                    return "connect: source and target must be existing nodes.";
                if (operation.Source == operation.Target)
                    return "connect: a node cannot connect to itself.";
                if (edges.Contains((operation.Source, operation.Target)))
                    return $"connect: '{operation.Source}' is already connected to '{operation.Target}'.";
                edges.Add((operation.Source, operation.Target));
                applied = Normalize(operation, null);
                return null;
            case "disconnect":
                if (operation.Source is null || operation.Target is null || !edges.Remove((operation.Source, operation.Target)))
                    return "disconnect: that connection does not exist.";
                applied = Normalize(operation, null);
                return null;
            case "update_config":
                return UpdateConfig(operation, nodes, out applied);
            case "rename_node":
                if (operation.NodeId is null || !nodes.TryGetValue(operation.NodeId, out var node))
                    return $"rename_node: node '{operation.NodeId}' does not exist.";
                if (string.IsNullOrWhiteSpace(operation.Label) || operation.Label.Length > MaxLabelLength)
                    return $"rename_node: label must be 1 to {MaxLabelLength} characters.";
                node.Label = operation.Label;
                applied = Normalize(operation, null);
                return null;
            default:
                return $"Unknown operation '{operation.Op}'.";
        }
    }

    private static string? AddNode(
        CanvasOperationArgs operation,
        Dictionary<string, SimNode> nodes,
        IReadOnlySet<string> allowedNodeTypes,
        out NormalizedCanvasOperation? applied)
    {
        applied = null;
        if (string.IsNullOrWhiteSpace(operation.Ref))
            return "add_node: ref is required.";
        if (nodes.ContainsKey(operation.Ref))
            return $"add_node: id '{operation.Ref}' is already used.";
        if (operation.Type is null || !allowedNodeTypes.Contains(operation.Type))
            return $"add_node: type '{operation.Type}' is not available.";
        if (operation.Label is { Length: > MaxLabelLength })
            return $"add_node: label must be at most {MaxLabelLength} characters.";
        if (operation.After is not null && !nodes.ContainsKey(operation.After))
            return $"add_node: after '{operation.After}' does not exist.";
        nodes[operation.Ref] = new SimNode(operation.Type, operation.Label ?? operation.Type, new JsonObject());
        applied = Normalize(operation, null);
        return null;
    }

    private static string? UpdateConfig(
        CanvasOperationArgs operation,
        Dictionary<string, SimNode> nodes,
        out NormalizedCanvasOperation? applied)
    {
        applied = null;
        if (operation.NodeId is null || !nodes.TryGetValue(operation.NodeId, out var node))
            return $"update_config: node '{operation.NodeId}' does not exist.";
        if (string.IsNullOrWhiteSpace(operation.ConfigJson))
            return "update_config: configJson is required.";
        if (operation.ConfigJson.Length > AssistantLimits.MaxNodeConfigBytes)
            return "update_config: configJson is too large.";
        JsonObject? patch;
        try
        {
            patch = JsonNode.Parse(operation.ConfigJson) as JsonObject;
        }
        catch (JsonException)
        {
            patch = null;
        }

        if (patch is null)
            return "update_config: configJson must be a JSON object.";
        foreach (var (key, value) in patch)
            node.Config[key] = value?.DeepClone();
        applied = Normalize(operation, JsonSerializer.SerializeToElement(patch));
        return null;
    }

    private static NormalizedCanvasOperation Normalize(CanvasOperationArgs operation, JsonElement? config) =>
        new(operation.Op!, operation.Ref, operation.Type, operation.Label, operation.After, operation.NodeId, operation.Source, operation.Target, config);

    private sealed class SimNode(string type, string label, JsonObject config)
    {
        public string Type { get; } = type;

        public string Label { get; set; } = label;

        public JsonObject Config { get; } = config;
    }
}

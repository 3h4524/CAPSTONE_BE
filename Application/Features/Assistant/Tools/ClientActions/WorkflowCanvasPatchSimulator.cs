using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Features.Assistant.Catalog;
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
    public CanvasPatchSimulation Simulate(
        AssistantCanvasDto canvas,
        IReadOnlyList<CanvasOperationArgs> operations,
        IReadOnlySet<string> allowedNodeTypes)
    {
        var errors = new List<string>();
        var normalized = new List<NormalizedCanvasOperation>();
        var nodes = LoadNodes(canvas);
        var edges = LoadEdges(canvas);

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

        return new CanvasPatchSimulation(normalized, ToDefinition(nodes, edges), errors);
    }

    public WorkflowDefinition Snapshot(AssistantCanvasDto canvas) => ToDefinition(LoadNodes(canvas), LoadEdges(canvas));

    private static Dictionary<string, SimNode> LoadNodes(AssistantCanvasDto canvas) =>
        canvas.Nodes.ToDictionary(
            node => node.Id,
            node => new SimNode(node.Type, node.Label, JsonNode.Parse(node.Config.GetRawText()) as JsonObject ?? new JsonObject()));

    private static List<(string Source, string Target)> LoadEdges(AssistantCanvasDto canvas) =>
        canvas.Edges.Select(edge => (edge.Source, edge.Target)).ToList();

    private static WorkflowDefinition ToDefinition(Dictionary<string, SimNode> nodes, List<(string Source, string Target)> edges) =>
        new(
            2,
            nodes.Select(pair => new WorkflowNode(
                pair.Key,
                pair.Value.Type,
                pair.Value.Label,
                new Point(),
                JsonSerializer.SerializeToElement(pair.Value.Config))).ToArray(),
            edges.Select(edge => new WorkflowEdge($"{edge.Source}->{edge.Target}", edge.Source, edge.Target)).ToArray());

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
                var issue = ConnectionIssue(operation.Source, operation.Target, nodes, edges);
                if (issue is not null)
                    return $"connect: {issue}";
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
        if (operation.After is not null && !nodes.ContainsKey(operation.After))
            return $"add_node: after '{operation.After}' does not exist.";
        JsonObject? patch = null;
        if (!string.IsNullOrWhiteSpace(operation.ConfigJson))
        {
            var invalid = ParseConfig(operation.Type, operation.ConfigJson, out patch);
            if (invalid is not null)
                return $"add_node: {invalid}";
        }

        var label = UniqueLabel(WorkflowNodeCatalog.LabelOf(operation.Type), nodes.Values.Select(node => node.Label));
        var config = new JsonObject();
        if (patch is not null)
            WorkflowNodeCatalog.DeepMerge(config, patch);
        nodes[operation.Ref] = new SimNode(operation.Type, label, config);
        applied = Normalize(operation with { Label = label }, patch is null ? null : JsonSerializer.SerializeToElement(patch));
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
        var invalid = ParseConfig(node.Type, operation.ConfigJson, out var patch);
        if (invalid is not null)
            return $"update_config: {invalid}";
        WorkflowNodeCatalog.DeepMerge(node.Config, patch!);
        applied = Normalize(operation, JsonSerializer.SerializeToElement(patch));
        return null;
    }

    private static string? ParseConfig(string nodeType, string configJson, out JsonObject? patch)
    {
        patch = null;
        if (configJson.Length > AssistantLimits.MaxNodeConfigBytes)
            return "configJson is too large.";
        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(configJson) as JsonObject;
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (parsed is null)
            return "configJson must be a JSON object.";
        parsed = WorkflowNodeCatalog.ExpandDottedKeys(parsed);
        var invalid = WorkflowNodeCatalog.ValidateConfig(nodeType, parsed);
        if (invalid is not null)
            return invalid;
        patch = parsed;
        return null;
    }

    private static string? ConnectionIssue(
        string source,
        string target,
        Dictionary<string, SimNode> nodes,
        List<(string Source, string Target)> edges)
    {
        var from = nodes[source];
        var to = nodes[target];
        var fromOrder = WorkflowNodeCatalog.OrderOf(from.Type);
        var toOrder = WorkflowNodeCatalog.OrderOf(to.Type);
        var fromOutput = WorkflowNodeCatalog.Outputs.Contains(from.Type);
        var toOutput = WorkflowNodeCatalog.Outputs.Contains(to.Type);
        // The outputs are peers: each follows the last step or another output, in any order.
        if (fromOutput && !toOutput && to.Type != "generate-listing")
            return $"Only another output may follow {from.Label}.";
        if (toOutput && !fromOutput && fromOrder >= 0)
        {
            var lastStep = nodes.Values
                .Where(node => !WorkflowNodeCatalog.Outputs.Contains(node.Type))
                .Select(node => WorkflowNodeCatalog.OrderOf(node.Type))
                .DefaultIfEmpty(-1)
                .Max();
            if (fromOrder != lastStep)
                return $"Outputs go after the last step: connect {WorkflowNodeCatalog.LabelOf(WorkflowNodeCatalog.RunOrder[lastStep])} to {to.Label}.";
        }
        else if (fromOrder >= 0 && toOrder >= 0 && !fromOutput)
        {
            var next = nodes.Values
                .Where(node => !WorkflowNodeCatalog.Outputs.Contains(node.Type))
                .Select(node => WorkflowNodeCatalog.OrderOf(node.Type))
                .Where(order => order > fromOrder)
                .OrderBy(order => order)
                .Cast<int?>()
                .FirstOrDefault();
            if (next != toOrder)
            {
                if (next is null)
                    return $"{from.Label} is the last step in the workflow, so it has no next node.";
                return $"Steps run in order. Connect {from.Label} to {WorkflowNodeCatalog.LabelOf(WorkflowNodeCatalog.RunOrder[next.Value])} next.";
            }
        }

        return ReachesSource(target, source, edges)
            ? "That connection would create a loop. Workflows run in one direction."
            : null;
    }

    private static bool ReachesSource(string start, string goal, List<(string Source, string Target)> edges)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<string>([start]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == goal)
                return true;
            if (!visited.Add(current))
                continue;
            foreach (var edge in edges.Where(item => item.Source == current))
                stack.Push(edge.Target);
        }

        return false;
    }

    public static string UniqueLabel(string baseLabel, IEnumerable<string> takenLabels)
    {
        var taken = takenLabels.Select(NormalizeLabel).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(NormalizeLabel(baseLabel)))
            return baseLabel;
        var suffix = 1;
        while (taken.Contains(NormalizeLabel($"{baseLabel} {suffix}")))
            suffix++;
        return $"{baseLabel} {suffix}";
    }

    private static string NormalizeLabel(string label) => label.Trim().ToLowerInvariant();

    private static NormalizedCanvasOperation Normalize(CanvasOperationArgs operation, JsonElement? config) =>
        new(operation.Op!, operation.Ref, operation.Type, operation.Label, operation.After, operation.NodeId, operation.Source, operation.Target, config);

    private sealed class SimNode(string type, string label, JsonObject config)
    {
        public string Type { get; } = type;

        public string Label { get; set; } = label;

        public JsonObject Config { get; } = config;
    }
}

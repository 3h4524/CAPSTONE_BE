using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools;

public enum AssistantToolKind
{
    Query,
    ClientAction,
    Confirmation,
    Interaction
}

public sealed record AssistantToolContext(Guid ConversationId, Guid UserId, AssistantCaller Caller, AssistantClientContextDto? Client);

public sealed record AssistantToolOutcome(string ModelResultJson, bool Succeeded, bool EndsTurn, IReadOnlyList<AssistantStreamEvent> Events, string? FinalText = null)
{
    public static AssistantToolOutcome Ok(object result, bool endsTurn = false, params AssistantStreamEvent[] events) =>
        new(AssistantJson.Truncate(AssistantJson.Serialize(result), AssistantLimits.MaxToolResultChars), true, endsTurn, events);

    public AssistantToolOutcome WithFinalText(string? finalText) =>
        string.IsNullOrWhiteSpace(finalText) && finalText != string.Empty ? this : this with { FinalText = finalText!.Trim() };

    public static AssistantToolOutcome Error(string code, string message) =>
        new(AssistantJson.Serialize(new { error = code, message }), false, false, []);
}

public sealed record AssistantConfirmedOutcome(string Summary, JsonElement Payload);

public interface IAssistantTool
{
    string Name { get; }

    AssistantToolKind Kind { get; }

    AssistantToolDeclaration Declaration { get; }

    bool IsAvailable(AssistantToolContext context);

    Task<AssistantToolOutcome> ExecuteAsync(AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken);
}

public interface IAssistantConfirmableTool : IAssistantTool
{
    Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(Guid pendingActionId, JsonElement arguments, CancellationToken cancellationToken);
}

public abstract class AssistantTool<TArgs> : IAssistantTool where TArgs : class
{
    public abstract string Name { get; }

    public abstract AssistantToolKind Kind { get; }

    protected abstract string Description { get; }

    protected abstract string ParametersSchemaJson { get; }

    public AssistantToolDeclaration Declaration =>
        new(Name, Description, AssistantToolSchemas.Build(ParametersSchemaJson, Kind));

    public abstract bool IsAvailable(AssistantToolContext context);

    public async Task<AssistantToolOutcome> ExecuteAsync(AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        TArgs? parsed;
        try
        {
            var source = arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? JsonDocument.Parse("{}").RootElement
                : arguments;
            parsed = source.Deserialize<TArgs>(AssistantJson.Options);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        return parsed is null
            ? AssistantToolOutcome.Error("invalid_arguments", "The arguments do not match the tool schema.")
            : await ExecuteCoreAsync(context, parsed, cancellationToken);
    }

    protected abstract Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, TArgs arguments, CancellationToken cancellationToken);
}

public sealed record NoArguments;

public static class AssistantToolSchemas
{
    public const string MoreRequestsProperty = "moreRequests";

    private const string MoreRequestsDescription =
        "True when the user's message still has other parts you have not handled yet (a question to answer or another change or action). You will be called again to finish them. False or omitted when this call completes the message.";

    public static JsonElement Build(string parametersSchemaJson, AssistantToolKind kind)
    {
        if (kind == AssistantToolKind.Query || JsonNode.Parse(parametersSchemaJson) is not JsonObject schema)
            return JsonDocument.Parse(parametersSchemaJson).RootElement.Clone();

        var properties = schema["properties"] as JsonObject ?? new JsonObject();
        properties[MoreRequestsProperty] = new JsonObject { ["type"] = "boolean", ["description"] = MoreRequestsDescription };
        schema["properties"] = properties;
        return JsonSerializer.SerializeToElement(schema);
    }

    public static bool RequestsMore(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(MoreRequestsProperty, out var value)
        && value.ValueKind == JsonValueKind.True;
}

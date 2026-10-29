using System.Text.Json;
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
        new(Name, Description, JsonDocument.Parse(ParametersSchemaJson).RootElement.Clone());

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

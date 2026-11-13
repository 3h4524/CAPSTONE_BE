namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantTurnEnd(string Status, AssistantErrorData? Error)
{
    public static readonly AssistantTurnEnd Done = new(AssistantMessageStatuses.Done, null);

    public static readonly AssistantTurnEnd Stopped =
        new(AssistantMessageStatuses.Stopped, new AssistantErrorData(AssistantErrors.StoppedCode, "Stopped."));

    public static readonly AssistantTurnEnd TimedOut =
        new(AssistantMessageStatuses.Error, new AssistantErrorData(AssistantErrors.TimeoutCode, "Podi took too long to answer. Please try again."));

    public static readonly AssistantTurnEnd Interrupted =
        new(AssistantMessageStatuses.Interrupted, new AssistantErrorData(AssistantErrors.InterruptedCode, "This answer was interrupted."));

    public static AssistantTurnEnd Failed(AssistantErrorData? error = null)
    {
        if (error is not null)
            return new(AssistantMessageStatuses.Error, error);
        var (code, message) = AssistantErrors.FromModelFailure(null);
        return new(AssistantMessageStatuses.Error, new AssistantErrorData(code, message));
    }

    public AssistantErrorData? StoredError => Status == AssistantMessageStatuses.Error ? Error : null;
}

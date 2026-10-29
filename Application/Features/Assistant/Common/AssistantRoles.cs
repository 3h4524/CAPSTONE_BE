namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantCaller(bool IsSeller, bool IsAdmin);

public static class AssistantMessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public static class AssistantMessageKinds
{
    public const string Chat = "chat";
    public const string ActionResult = "action_result";
}

public static class AssistantPendingActionStatuses
{
    public const string Pending = "pending";
    public const string Executing = "executing";
    public const string Executed = "executed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Failed = "failed";
}

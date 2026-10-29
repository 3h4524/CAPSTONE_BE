namespace APCS.Application.Features.Assistant.Common;

public static class AssistantLimits
{
    public const int MaxMessageLength = 4000;
    public const int MaxRouteLength = 300;
    public const int MaxSelectedNodeIds = 50;
    public const int MaxIdentifierLength = 100;
    public const int MaxToolIterations = 5;
    public const int HistoryWindow = 20;
    public const int PendingActionTtlMinutes = 15;
    public const int MaxCanvasOperations = 20;
    public const int MaxCanvasNodes = 40;
    public const int MaxCanvasEdges = 80;
    public const int MaxNodeConfigBytes = 8192;
    public const int MaxToolResultChars = 12000;
    public const int ConversationListSize = 20;
    public const int TitleLength = 60;
}

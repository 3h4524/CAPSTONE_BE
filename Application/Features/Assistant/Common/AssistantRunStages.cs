namespace APCS.Application.Features.Assistant.Common;

public static class AssistantRunStages
{
    public const string NotStarted = "not_started";
    public const string Designs = "designs";
    public const string DesignApproval = "design_approval";
    public const string DesignsReady = "designs_ready";
    public const string MockupApproval = "mockup_approval";
    public const string Video = "video";
    public const string VideoReview = "video_review";
    public const string Listing = "listing";
    public const string Export = "export";
    public const string Publish = "publish";
    public const string Done = "done";
}

public static class AssistantRunStates
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string WaitingForUser = "waiting_for_user";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Done = "done";
}

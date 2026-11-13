namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    // The video steps of a run, in order. What the run makes then goes to its outputs (OutputSteps).
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video"];
    // A run that publishes without a video: the seller still approves the mock-ups, and the publish steps follow.
    public static readonly string[] PublishPipeline = ["product-input", "apply-mockup", "approval-gate"];
    // The canvas makes the designs as a batch job before the run starts, so these steps have no executor here:
    // a workflow may place them between Product Input and Apply Mockup, and a run leaves them untouched.
    public static readonly string[] BatchSteps = ["prompt-synthesis", "design-image", "design-approval"];
    public static readonly string[] ListingSteps = ["generate-listing"];
    // A run's outputs: peers that each take what the run made (the approved mock-ups, and the approved video of a video
    // run), in no order and independent of one another. A workflow has at least one and each at most once; they hang
    // off the last step (Review Video, or Mockup Approval without a video), side by side or one after another, and run
    // together: one failing does not stop the others.
    public static readonly string[] OutputSteps = ["export-zip", "publish-printify", "publish-etsy", "publish-social"];
    // The outputs that run in the background through WorkflowPublishService; Export ZIP is a media job.
    public static readonly string[] PublishSteps = ["publish-printify", "publish-etsy", "publish-social"];
    // The steps before the outputs that must be connected in this order. Listing Content is not one of them: it needs no approval.
    public static readonly string[] CoreOrder = ["product-input", .. BatchSteps, .. Pipeline.Skip(1)];
    // Every step in the order a run shows them.
    public static readonly string[] RunOrder = [.. CoreOrder, .. OutputSteps];
    public WorkflowCapabilities Get() => new(2,
        Pipeline.Append("export-zip").Select(type => new NodeCapability(type, true, "Standard MVP executor"))
            .Concat(BatchSteps.Select(type => new NodeCapability(type, true, "Runs as a batch job from the canvas.")))
            .Concat(new[] { new NodeCapability("generate-listing", true, "Generates listing content with Gemini."),
                new NodeCapability("publish-printify", true, "Creates the product on Printify, which publishes it to Etsy and fulfils its orders."),
                new NodeCapability("publish-etsy", true, "Creates an Etsy draft listing (a digital download, or a physical item without a print partner)."),
                new NodeCapability("publish-social", true, "Prepares Pinterest and TikTok posts from the approved mock-ups, video and listing.") }).ToArray(),
        [new("standard", true, "available", "Showcase from approved mockups. No AI model cost.", true),
         new("ai_shot", true, "available", "Build a video from individually configured AI scenes.", false,
             new CostRange("USD", VideoDurationPolicy.MinimumAiSceneSeconds * AiVideoModel.LitePricePerSecondUsd, VideoDurationPolicy.MaximumVideoSeconds * AiVideoModel.PricePerSecondUsd,
                 "Estimated cost for new clips is $0.04 or $0.08 per second at 768P; confirmation is required."))]);
    public bool IsModeEnabled(string mode) => Get().VideoModes.Any(x => x.Mode == mode && x.Enabled);
}

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video", "export-zip"];
    // Steps of the video pipeline a workflow may leave out: a run then goes on from Review Video to the publish steps.
    public static readonly string[] OptionalVideoSteps = ["export-zip"];
    // A run that publishes without a video: the seller still approves the mock-ups, and the publish steps follow.
    public static readonly string[] PublishPipeline = ["product-input", "apply-mockup", "approval-gate"];
    // The canvas makes the designs as a batch job before the run starts, so these steps have no executor here:
    // a workflow may place them between Product Input and Apply Mockup, and a run leaves them untouched.
    public static readonly string[] BatchSteps = ["prompt-synthesis", "design-image", "design-approval"];
    public static readonly string[] ListingSteps = ["generate-listing"];
    // Push to Printify makes the product from the design and its approved mock-ups (no video) and publishes it to Etsy:
    // it branches off Apply Mockup or Mockup Approval and starts right after Mockup Approval, beside the video: neither
    // waits for the other and one failing does not stop the other. Publish to
    // Etsy (a direct listing, meant for a digital download) is last in a run: after Review Video (and Export ZIP, when
    // there is one), or right after Mockup Approval without a video. Each runs in the background through
    // WorkflowPublishService.
    public const string PrintifyStep = "publish-printify";
    public static readonly string[] PublishSteps = [PrintifyStep, "publish-etsy"];
    // The order a run goes through its steps. The steps of the line must be connected in this order; Listing Content is
    // not one of them (it needs no approval), and neither is Push to Printify, which branches off the line and starts
    // right after Mockup Approval, beside the video.
    public static readonly string[] RunOrder =
        ["product-input", .. BatchSteps, "apply-mockup", "approval-gate", PrintifyStep, .. Pipeline.Skip(3), "publish-etsy"];
    public WorkflowCapabilities Get() => new(2,
        Pipeline.Select(type => new NodeCapability(type, true, "Standard MVP executor"))
            .Concat(BatchSteps.Select(type => new NodeCapability(type, true, "Runs as a batch job from the canvas.")))
            .Concat(new[] { new NodeCapability("generate-listing", true, "Generates listing content with Gemini."),
                new NodeCapability("publish-printify", true, "Creates the product on Printify, which publishes it to Etsy and fulfils its orders."),
                new NodeCapability("publish-etsy", true, "Creates an Etsy draft listing (a digital download, or a physical item without a print partner).") }).ToArray(),
        [new("standard", true, "available", "Showcase from approved mockups. No AI model cost.", true),
         new("ai_shot", true, "available", "Build a video from individually configured AI scenes.", false,
             new CostRange("USD", VideoDurationPolicy.MinimumAiSceneSeconds * AiVideoModel.LitePricePerSecondUsd, VideoDurationPolicy.MaximumVideoSeconds * AiVideoModel.PricePerSecondUsd,
                 "Estimated cost for new clips is $0.04 or $0.08 per second at 768P; confirmation is required."))]);
    public bool IsModeEnabled(string mode) => Get().VideoModes.Any(x => x.Mode == mode && x.Enabled);
}

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video", "export-zip"];
    // The canvas makes the designs as a batch job before the run starts, so these steps have no executor here:
    // a workflow may place them between Product Input and Apply Mockup, and a run leaves them untouched.
    public static readonly string[] BatchSteps = ["prompt-synthesis", "design-image", "design-approval"];
    public static readonly string[] ListingSteps = ["generate-listing"];
    // After the approved package: Printify makes the product and publishes it to Etsy, then a direct Etsy listing
    // (meant for a digital download). Each runs in the background through WorkflowPublishService.
    public static readonly string[] PublishSteps = ["publish-printify", "publish-etsy"];
    // The steps of a run that must be connected in this order. Listing Content is not one of them: it needs no approval.
    public static readonly string[] RunOrder = ["product-input", .. BatchSteps, .. Pipeline.Skip(1), .. PublishSteps];
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

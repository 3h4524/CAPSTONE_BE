namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video", "export-zip"];
    // The canvas makes the designs as a batch job before the run starts, so these steps have no executor here:
    // a workflow may place them between Product Input and Apply Mockup, and a run leaves them untouched.
    public static readonly string[] BatchSteps = ["prompt-synthesis", "design-image", "design-approval"];
    public static readonly string[] ListingSteps = ["generate-listing"];
    // The steps of a run that must be connected in this order. Listing Content is not one of them: it needs no approval.
    public static readonly string[] RunOrder = ["product-input", .. BatchSteps, .. Pipeline.Skip(1)];
    public WorkflowCapabilities Get() => new(2,
        Pipeline.Select(type => new NodeCapability(type, true, "Standard MVP executor"))
            .Concat(BatchSteps.Select(type => new NodeCapability(type, true, "Runs as a batch job from the canvas.")))
            .Concat(new[] { new NodeCapability("generate-listing", true, "Generates listing content with Gemini."),
                new NodeCapability("publish-etsy", false, "Backend executor is not available yet."),
                new NodeCapability("publish-printify", false, "Backend executor is not available yet.") }).ToArray(),
        [new("standard", true, "available", "Showcase from approved mockups. No AI model cost.", true),
         new("ai_shot", true, "available", $"Build a video from individually configured AI scenes using {AiVideoModel.DisplayName}.", false,
             new CostRange("USD", 4 * AiVideoModel.PricePerSecondUsd, 30 * AiVideoModel.PricePerSecondUsd,
                 $"Estimated {AiVideoModel.DisplayName} cost at ${AiVideoModel.PricePerSecondUsd:0.00}/second for new clips; confirmation is required."))]);
    public bool IsModeEnabled(string mode) => Get().VideoModes.Any(x => x.Mode == mode && x.Enabled);
}

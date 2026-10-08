namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video", "export-zip"];
    // The canvas makes the designs as a batch job before the run starts, so these steps have no executor here:
    // a workflow may place them between Product Input and Apply Mockup, and a run leaves them untouched.
    public static readonly string[] BatchSteps = ["prompt-synthesis", "design-image", "design-approval"];
    // Every step a run may contain, in the order they must be connected.
    public static readonly string[] RunOrder = ["product-input", .. BatchSteps, .. Pipeline.Skip(1)];
    public WorkflowCapabilities Get() => new(2,
        Pipeline.Select(type => new NodeCapability(type, true, "Standard MVP executor"))
            .Concat(BatchSteps.Select(type => new NodeCapability(type, true, "Runs as a batch job from the canvas.")))
            .Concat(new[] { "generate-listing", "publish-etsy", "publish-printify" }
                .Select(type => new NodeCapability(type, false, "Backend executor is not available yet."))).ToArray(),
        [new("standard", true, "available", "Showcase from approved mockups. No AI model cost.", true),
         new("ai_background", false, "coming_soon", "Preserve the product and generate a new background.", true),
         new("ai_shot", false, "coming_soon", "One image-to-video highlight with Standard scenes.", true)]);
    public bool IsModeEnabled(string mode) => Get().VideoModes.Any(x => x.Mode == mode && x.Enabled);
}

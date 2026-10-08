namespace APCS.Application.Features.Workflows;

public sealed class WorkflowCapabilityRegistry
{
    public static readonly string[] Pipeline = ["product-input", "apply-mockup", "approval-gate", "generate-video", "review-video", "export-zip"];
    public WorkflowCapabilities Get() => new(2,
        Pipeline.Select(type => new NodeCapability(type, true, "Standard MVP executor"))
            .Concat(new[] { "prompt-synthesis", "design-image", "generate-listing", "publish-etsy", "publish-printify" }
                .Select(type => new NodeCapability(type, false, "Backend executor is not available yet."))).ToArray(),
        [new("standard", true, "available", "Showcase from approved mockups. No AI model cost.", true),
         new("ai_background", false, "coming_soon", "Preserve the product and generate a new background.", true),
         new("ai_shot", false, "coming_soon", "One image-to-video highlight with Standard scenes.", true)]);
    public bool IsModeEnabled(string mode) => Get().VideoModes.Any(x => x.Mode == mode && x.Enabled);
}

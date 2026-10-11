using System.Text.Json;
using APCS.Application.Features.Workflows;

namespace APCS.Api.UnitTests.TestSupport;

internal static class VideoWorkflowTestData
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    public static WorkflowResponse Workflow() => new(Guid.NewGuid(), "Product video", "", 6, 1, Now, Now, new(2, [], []));
    public static RunResponse Run() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "waiting_for_input", 1, 1, Now, null, []);
    public static SaveWorkflowRequest SaveRequest() => new("Product video", "", new(2, [], []));
    public static WorkerJob Job() => new(Guid.NewGuid(), Guid.NewGuid(), "render", JsonSerializer.SerializeToElement(new { }), [], new Dictionary<string, UploadGrant>());
    public static Storyboard Board() => new("product_showcase", 1, 12, "tshirt", [], "fixed-fingerprint");
    public static MockupResponse Mockup() => new(Guid.NewGuid(), Guid.NewGuid(), "uploaded", "Hero", "artwork", null, 1, "pending", null, 1080, 2160, new(), "https://example.invalid/source", []);
}

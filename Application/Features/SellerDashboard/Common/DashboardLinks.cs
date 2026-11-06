using System.Text.Json;

namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>
/// Builds the frontend links the Dashboard sends the Seller to, so each one opens the exact place
/// where the work waits rather than a list to search through.
/// </summary>
public static class DashboardLinks
{
    /// <summary>The Batches page's product filters, as <c>batch-workspace</c> names them.</summary>
    public static class BatchFilters
    {
        public const string Review = "review";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Processing = "processing";
    }

    /// <summary>
    /// A batch job: inside its workflow when it was started from one (<c>config.workflowId</c>), where
    /// its designs, mock-ups and approvals are; otherwise its own page.
    /// </summary>
    public static string BatchJob(Guid jobId, string? config) =>
        WorkflowIdOf(config) is Guid workflowId
            ? $"/workflows?id={workflowId}&job={jobId}"
            : $"/batch-jobs/{jobId}";

    /// <summary>
    /// One product's run of a workflow: its Generate Listing step (listing and SEO score), Review Video
    /// and the rest of the steps run for that product.
    /// </summary>
    public static string WorkflowRun(Guid workflowId, Guid runId) => $"/workflows?id={workflowId}&run={runId}";

    public static string Workflow(Guid workflowId) => $"/workflows?id={workflowId}";

    /// <summary>A batch on the Batches page, its products filtered to one status when given.</summary>
    public static string Batch(Guid batchId, string? filter = null) =>
        filter is null ? $"/batches?batch={batchId}" : $"/batches?batch={batchId}&status={filter}";

    /// <summary>The workflow a batch job was started from, read from its config; null when there is none.</summary>
    public static Guid? WorkflowIdOf(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;
        try
        {
            using var document = JsonDocument.Parse(config);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("workflowId", out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out var workflowId)
                    ? workflowId
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

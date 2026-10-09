using APCS.Common.Models;

namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>Creates expected errors returned by the design-generation use case.</summary>
public static class DesignGenerationErrors
{
    public static Error Unauthenticated(string action) =>
        Error.Unauthorized("DesignGeneration.Unauthenticated", $"Please sign in to {action} image generation.");

    public static Error BatchNotFound() =>
        Error.NotFound("DesignGeneration.BatchNotFound", "The batch was not found.");

    public static Error JobNotFound() =>
        Error.NotFound("DesignGeneration.JobNotFound", "The batch job was not found.");

    public static Error NotRetryable() =>
        Error.Conflict("DesignGeneration.NotRetryable", "Failed products can only be retried after the job has finished.");

    public static Error NotCancellable() =>
        Error.Conflict("DesignGeneration.NotCancellable", "Only a queued or running job can be cancelled.");

    public static Error ApprovalNotAvailable() =>
        Error.Conflict("DesignGeneration.ApprovalNotAvailable", "Images can be approved or rejected once the job has finished generating.");

    public static Error ImageNotFound() =>
        Error.NotFound("DesignGeneration.ImageNotFound", "One of the images was not found in this job.");

    public static Error NoFailedProducts() =>
        Error.Conflict("DesignGeneration.NoFailedProducts", "This job has no failed products to retry.");

    public static Error NotResettable() =>
        Error.Conflict("DesignGeneration.NotResettable",
            "Only a failed product, or one whose designs were all rejected, can be set back to pending. Reject the designs that are still waiting first.");

    public static Error NothingToReset() =>
        Error.Conflict("DesignGeneration.NothingToReset", "This batch has no failed product, and none whose designs were all rejected.");

    public static Error ProductNotFound() =>
        Error.NotFound("DesignGeneration.ProductNotFound", "One of the products was not found in this batch.");

    public static Error NotDraft() =>
        Error.Conflict("DesignGeneration.NotDraft", "Image generation can only be started from a draft batch job.");

    public static Error NoPendingProducts() =>
        Error.Conflict("Batches.NoPendingProducts", "This batch job has no pending products to generate.");

    // BR51.
    public static Error MissingApiKey() =>
        new("MSG35", "Connect a valid Gemini API key before starting image generation.", ErrorType.Conflict);

    // BR52.
    public static Error ActiveJobExists() =>
        new("MSG36", "Another job is already running for this batch.", ErrorType.Conflict);

    public static Error NoActivePlan() =>
        Error.Conflict("DesignGeneration.NoActivePlan", "An active subscription plan is required to generate images.");

    // BR50.
    public static Error InsufficientQuota() =>
        new("MSG34", "You do not have enough image-generation quota left this period. Upgrade your plan or reduce the number of products/variations.", ErrorType.Conflict);

    public static Error TemplateNotFound() =>
        Error.NotFound("DesignGeneration.TemplateNotFound", "The selected design template was not found.");

    public static Error StyleNotFound() =>
        Error.NotFound("DesignGeneration.StyleNotFound", "The selected style preset was not found.");
}

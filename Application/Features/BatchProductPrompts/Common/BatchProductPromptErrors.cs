using APCS.Common.Models;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>Creates expected errors returned by per-row prompt use cases.</summary>
public static class BatchProductPromptErrors
{
    public static Error Unauthenticated(string action) =>
        Error.Unauthorized("BatchProductPrompts.Unauthenticated", $"Please sign in to {action} design prompts.");

    public static Error RowNotFound() =>
        Error.NotFound("BatchProductPrompts.RowNotFound", "The product row was not found.");

    public static Error NotPending() =>
        Error.Conflict("BatchProductPrompts.NotPending", "Only rows waiting for processing can be edited.");

    public static Error TooLong(int length) =>
        Error.Validation($"The effective prompt must not exceed 1000 characters. It is {length} characters long.");
}

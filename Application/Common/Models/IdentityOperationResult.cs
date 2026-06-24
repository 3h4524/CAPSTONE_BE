namespace APCS.Application.Common.Models;

/// <summary>
/// Represents the outcome of an identity operation.
/// </summary>
/// <param name="Succeeded">Indicates whether the operation succeeded.</param>
/// <param name="Errors">The user-safe operation errors.</param>
public sealed record IdentityOperationResult(bool Succeeded, IReadOnlyCollection<string> Errors)
{
    /// <summary>
    /// Creates a successful identity result.
    /// </summary>
    public static IdentityOperationResult Success() => new(true, Array.Empty<string>());

    /// <summary>
    /// Creates a failed identity result.
    /// </summary>
    public static IdentityOperationResult Failure(IReadOnlyCollection<string> errors) => new(false, errors);
}

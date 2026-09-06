using APCS.Common.Constants;

namespace APCS.Common.Models;

/// <summary>
/// Represents an expected application error.
/// </summary>
/// <param name="Code">The stable error code.</param>
/// <param name="Message">The user-safe error message.</param>
/// <param name="Type">The error category.</param>
/// <param name="Details">Optional structured error details.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? Details = null)
{
    /// <summary>
    /// Represents the absence of an error.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    public static Error Validation(string message, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(ErrorCodes.Validation, message, ErrorType.Validation, details);

    /// <summary>
    /// Creates an unauthorized error.
    /// </summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>
    /// Creates a forbidden error.
    /// </summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>
    /// Creates a not-found error.
    /// </summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>
    /// Creates a conflict error.
    /// </summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>
    /// Creates an unexpected failure error.
    /// </summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}

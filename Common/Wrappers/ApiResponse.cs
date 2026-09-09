namespace APCS.Common.Wrappers;

/// <summary>
/// Represents a standard API response envelope for endpoints that choose to use one.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <param name="Succeeded">Indicates whether the request succeeded.</param>
/// <param name="Data">The response payload.</param>
/// <param name="Message">An optional response message.</param>
/// <param name="Errors">Optional error messages.</param>
public sealed record ApiResponse<T>(
    bool Succeeded,
    T? Data,
    string? Message = null,
    IReadOnlyList<string>? Errors = null)
{
    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static ApiResponse<T> Success(T data, string? message = null) => new(true, data, message);

    /// <summary>
    /// Creates a failed response.
    /// </summary>
    public static ApiResponse<T> Failure(IReadOnlyList<string> errors, string? message = null) => new(false, default, message, errors);
}

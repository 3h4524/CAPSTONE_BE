namespace APCS.Common.Models;

/// <summary>
/// Describes how an application error should be mapped to an API response.
/// </summary>
public enum ErrorType
{
    None = 0,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Failure
}

using APCS.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Extensions;

/// <summary>
/// Maps application results to HTTP responses.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a result to an action result.
    /// </summary>
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.Ok(result.Value)
            : ToProblemDetails(result.Error);
    }

    /// <summary>
    /// Converts a non-generic result to an action result.
    /// </summary>
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.NoContent()
            : ToProblemDetails(result.Error);
    }

    private static IActionResult ToProblemDetails(Error error)
    {
        var statusCode = GetStatusCode(error.Type);
        var problem = new ProblemDetails
        {
            Type = $"https://apcs/errors/{error.Code}",
            Title = error.Message,
            Detail = error.Message,
            Status = statusCode
        };

        problem.Extensions["code"] = error.Code;

        if (error.Details is not null)
        {
            problem.Extensions["errors"] = error.Details;
        }

        return new ObjectResult(problem)
        {
            StatusCode = statusCode
        };
    }

    private static int GetStatusCode(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}

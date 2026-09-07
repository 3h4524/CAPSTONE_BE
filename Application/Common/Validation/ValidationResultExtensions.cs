using APCS.Common.Models;
using FluentValidation.Results;

namespace APCS.Application.Common.Validation;

/// <summary>
/// Converts FluentValidation results into the established <see cref="Result"/> error shape.
/// </summary>
public static class ValidationResultExtensions
{
    /// <summary>
    /// Groups validation failures by property name into a single validation <see cref="Error"/>.
    /// </summary>
    public static Error ToValidationError(this ValidationResult validationResult)
    {
        var details = validationResult.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        return Error.Validation("One or more validation errors occurred.", details);
    }
}

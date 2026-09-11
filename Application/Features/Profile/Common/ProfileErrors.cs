using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Profile.Common;

/// <summary>
/// Builds every error the Profile feature can return, reusing canonical codes.
/// </summary>
internal static class ProfileErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.");

    public static Error UserNotFound() =>
        Error.NotFound(ErrorCodes.UserNotFound, "User was not found.");

    public static Error EmailAlreadyExists() =>
        Error.Conflict(ErrorCodes.EmailAlreadyExists, "Email is already registered.");
}

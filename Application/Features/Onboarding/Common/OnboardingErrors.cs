using APCS.Common.Models;

namespace APCS.Application.Features.Onboarding.Common;

internal static class OnboardingErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized("auth.unauthorized", "The request is not authenticated.");

    public static Error ProgressUnavailable() =>
        Error.Failure("onboarding.unavailable", "Onboarding progress is not available yet. Apply the pending migration and try again.");
}

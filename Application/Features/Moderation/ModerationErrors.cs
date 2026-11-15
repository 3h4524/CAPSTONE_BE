using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using APCS.Common.Models;

namespace APCS.Application.Features.Moderation;

public static class ModerationErrors
{
    public const string ContentPolicyViolationCode = "ContentPolicyViolation";
    public const string ModerationPendingCode = "ModerationPending";

    /// <summary>The content was refused under the platform's content policy.</summary>
    public static Error Blocked(ModerationVerdict verdict) =>
        new(ContentPolicyViolationCode, ModerationPolicy.BlockedMessage(verdict), ErrorType.Validation);

    /// <summary>An image whose content check could not run yet cannot be approved.</summary>
    public static Error Pending() =>
        new(ModerationPendingCode,
            "This image has not passed the content check yet because the check was unavailable. Try approving it again in a moment.",
            ErrorType.Conflict);
}

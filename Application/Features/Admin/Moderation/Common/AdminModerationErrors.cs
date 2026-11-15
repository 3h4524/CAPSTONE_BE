using APCS.Common.Models;

namespace APCS.Application.Features.Admin.Moderation.Common;

public static class AdminModerationErrors
{
    public static Error StorageMissing() => Error.Conflict("ModerationStorageMissing",
        "The moderation tables do not exist in this database yet. Run the content moderation migration first.");

    public static Error CheckNotFound() => Error.NotFound("ModerationCheckNotFound", "This moderation check was not found.");

    public static Error TermNotFound() => Error.NotFound("ModerationTermNotFound", "This blocklist term was not found.");

    public static Error DuplicateTerm() => Error.Conflict("ModerationTermDuplicate", "This term is already in the blocklist.");

    public static Error AllowlistNotFound() => Error.NotFound("ModerationAllowlistNotFound", "This allowlisted phrase was not found.");

    public static Error DuplicateAllowlist() => Error.Conflict("ModerationAllowlistDuplicate", "This phrase is already in the allowlist.");
}

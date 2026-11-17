using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SystemApiKeys.Common;

/// <summary>Every error the Admin's platform key page can return.</summary>
internal static class SystemApiKeyAdminErrors
{
    public static Error KeyNotFound() =>
        Error.NotFound("SystemApiKeys.NotFound", "This key no longer exists. Reload the page.");

    public static Error ProviderNotFound() =>
        Error.NotFound("SystemApiKeys.ProviderNotFound", "This provider is not one the platform keeps keys for.");

    public static Error Duplicate() =>
        Error.Conflict("SystemApiKeys.Duplicate", "This key is already on the list.");

    public static Error ChangedByAnotherAdmin() =>
        Error.Conflict("SystemApiKeys.Changed", "Another Admin changed this since you opened it. Reload the page and try again.");

    public static Error Refused(string provider) =>
        Error.Validation($"{provider} did not accept this key. Check that it is copied in full and that the account can use the API.");

    public static Error Unreadable() =>
        Error.Validation("This server cannot decrypt this key. Delete it and add the key again.");

    public static Error OrderMismatch() =>
        Error.Validation("The order must name every key of the provider once. Reload the page and try again.");
}

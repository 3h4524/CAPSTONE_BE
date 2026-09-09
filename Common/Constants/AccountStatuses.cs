namespace APCS.Common.Constants;

/// <summary>
/// Provides the account lifecycle states persisted on a user record.
/// </summary>
public static class AccountStatuses
{
    /// <summary>
    /// The account is usable.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// The account was locked by an administrator.
    /// </summary>
    public const string Locked = "locked";

    /// <summary>
    /// The account was suspended, typically for billing or policy reasons.
    /// </summary>
    public const string Suspended = "suspended";

    /// <summary>
    /// The account was created but the email address is not verified yet.
    /// </summary>
    public const string PendingVerification = "pending_verification";

    /// <summary>
    /// Gets every valid account status.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } =
        [Active, Locked, Suspended, PendingVerification];
}

namespace APCS.Common.Constants;

/// <summary>
/// Provides the account lifecycle states persisted on a user record.
/// </summary>
/// <remarks>
/// Distinct from Identity's automatic lockout: <see cref="Locked"/> is set deliberately by an
/// administrator and only an administrator clears it, whereas a lockout expires on its own once
/// <c>LockoutEnd</c> passes.
/// </remarks>
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

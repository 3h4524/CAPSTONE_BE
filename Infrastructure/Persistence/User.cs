using System.ComponentModel.DataAnnotations.Schema;
using APCS.Common.Constants;
using APCS.Domain.Common;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Represents an authenticated APCS account.
/// </summary>
/// <remarks>
/// Lives in Infrastructure because it inherits <see cref="IdentityUser{TKey}"/>; identity
/// framework types are not allowed in Domain. Administrators are ordinary users holding the
/// admin role rather than a separate entity.
/// </remarks>
public sealed class User : IdentityUser<Guid>, IHasCreationTime, IHasModificationTime, ISoftDeletable
{
    /// <summary>
    /// Initializes a new user with a client-generated identifier.
    /// </summary>
    public User()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Gets or sets the account holder's full name.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the linked Google OAuth identifier.
    /// </summary>
    public string? OAuthGoogleId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth provider name.
    /// </summary>
    public string? OAuthProvider { get; set; }

    /// <summary>
    /// Gets or sets the avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the account status.
    /// </summary>
    public string AccountStatus { get; set; } = AccountStatuses.Active;

    /// <summary>
    /// Gets or sets the UTC email verification timestamp.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last successful login.
    /// </summary>
    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>
    /// Gets a value indicating whether the account can be used.
    /// </summary>
    [NotMapped]
    public bool IsActive =>
        DeletedAtUtc is null && string.Equals(AccountStatus, AccountStatuses.Active, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the user profile.
    /// </summary>
    public UserProfile? Profile { get; set; }

    /// <summary>
    /// Gets the API keys owned by the user.
    /// </summary>
    public ICollection<ApiKey> ApiKeys { get; } = new List<ApiKey>();

    /// <summary>
    /// Gets the subscriptions owned by the user.
    /// </summary>
    public ICollection<Subscription> Subscriptions { get; } = new List<Subscription>();

    /// <summary>
    /// Gets the payment methods owned by the user.
    /// </summary>
    public ICollection<PaymentMethod> PaymentMethods { get; } = new List<PaymentMethod>();

    /// <summary>
    /// Gets the invoices issued to the user.
    /// </summary>
    public ICollection<Invoice> Invoices { get; } = new List<Invoice>();

    /// <summary>
    /// Gets the usage statistics recorded for the user.
    /// </summary>
    public ICollection<UsageStatistic> UsageStatistics { get; } = new List<UsageStatistic>();
}

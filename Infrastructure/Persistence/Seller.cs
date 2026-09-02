using System.ComponentModel.DataAnnotations.Schema;
using APCS.Domain.Common;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Represents an authenticated APCS seller.
/// </summary>
public sealed class Seller : IdentityUser<int>, IHasCreationTime, IHasModificationTime, ISoftDeletable
{
    /// <summary>
    /// Gets or sets the seller's full name.
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
    /// Gets or sets the seller's time zone.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Gets or sets the seller's preferred language.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Gets or sets the seller account status.
    /// </summary>
    public string AccountStatus { get; set; } = "active";

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
    /// Gets a value indicating whether the seller account can be used.
    /// </summary>
    [NotMapped]
    public bool IsActive =>
        DeletedAtUtc is null && string.Equals(AccountStatus, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the seller profile.
    /// </summary>
    public SellerProfile? Profile { get; set; }

    /// <summary>
    /// Gets API keys owned by the seller.
    /// </summary>
    public ICollection<ApiKey> ApiKeys { get; } = new List<ApiKey>();

    /// <summary>
    /// Gets subscriptions owned by the seller.
    /// </summary>
    public ICollection<Subscription> Subscriptions { get; } = new List<Subscription>();

    /// <summary>
    /// Gets payment methods owned by the seller.
    /// </summary>
    public ICollection<PaymentMethod> PaymentMethods { get; } = new List<PaymentMethod>();

    /// <summary>
    /// Gets invoices issued to the seller.
    /// </summary>
    public ICollection<Invoice> Invoices { get; } = new List<Invoice>();

    /// <summary>
    /// Gets usage statistics recorded for the seller.
    /// </summary>
    public ICollection<UsageStatistic> UsageStatistics { get; } = new List<UsageStatistic>();
}

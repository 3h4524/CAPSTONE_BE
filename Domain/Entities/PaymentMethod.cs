using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Stores a tokenized user payment method without sensitive card data.
/// </summary>
public sealed class PaymentMethod : CreationTrackedSoftDeletableEntity
{
    private PaymentMethod()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the payment method type.
    /// </summary>
    public string PaymentType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the external Stripe payment method identifier.
    /// </summary>
    public string? StripePaymentMethodId { get; private set; }

    /// <summary>
    /// Gets the encrypted PayPal email address.
    /// </summary>
    public string? PaypalEmailEncrypted { get; private set; }

    /// <summary>
    /// Gets the final four card digits.
    /// </summary>
    public string? CardLast4Digits { get; private set; }

    /// <summary>
    /// Gets the card brand.
    /// </summary>
    public string? CardBrand { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is the default payment method.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the payment method is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;
}

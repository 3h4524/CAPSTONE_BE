using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a subscription billing invoice.
/// </summary>
public sealed class Invoice : AuditableEntity
{
    private Invoice()
    {
    }

    /// <summary>
    /// Gets the billed subscription identifier.
    /// </summary>
    public int SubscriptionId { get; private set; }

    /// <summary>
    /// Gets the billed seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the unique public invoice number.
    /// </summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the invoice issue date.
    /// </summary>
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>
    /// Gets the invoice due date.
    /// </summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>
    /// Gets the pre-tax amount in USD.
    /// </summary>
    public decimal AmountUsd { get; private set; }

    /// <summary>
    /// Gets the tax amount in USD.
    /// </summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>
    /// Gets the total amount in USD.
    /// </summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// Gets the invoice status.
    /// </summary>
    public string Status { get; private set; } = "draft";

    /// <summary>
    /// Gets the payment date.
    /// </summary>
    public DateOnly? PaymentDate { get; private set; }

    /// <summary>
    /// Gets invoice line items as JSON.
    /// </summary>
    public string Items { get; private set; } = "[]";

    /// <summary>
    /// Gets the generated invoice PDF URL.
    /// </summary>
    public string? PdfUrl { get; private set; }

    /// <summary>
    /// Gets the external Stripe invoice identifier.
    /// </summary>
    public string? StripeInvoiceId { get; private set; }

    /// <summary>
    /// Gets the billed subscription.
    /// </summary>
    public Subscription Subscription { get; private set; } = null!;
}

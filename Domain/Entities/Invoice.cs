using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an invoice issued for a subscription.
/// </summary>
public sealed class Invoice : AuditableEntity
{
    private Invoice()
    {
    }

    /// <summary>
    /// Gets the billed subscription identifier.
    /// </summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>
    /// Gets the billed user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the human-readable invoice number.
    /// </summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the invoice issue date.
    /// </summary>
    public DateOnly InvoiceDate { get; private set; }

    /// <summary>
    /// Gets the payment due date.
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
    /// Gets the total payable amount in USD.
    /// </summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>
    /// Gets the invoice status.
    /// </summary>
    public string Status { get; private set; } = "draft";

    /// <summary>
    /// Gets the date the invoice was paid.
    /// </summary>
    public DateOnly? PaymentDate { get; private set; }

    /// <summary>
    /// Gets the invoice line items as JSON.
    /// </summary>
    public string Items { get; private set; } = "[]";

    /// <summary>
    /// Gets the generated PDF URL.
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

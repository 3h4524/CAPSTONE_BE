using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Invoice
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid UserId { get; set; }

    public string InvoiceNumber { get; set; } = null!;

    public DateOnly InvoiceDate { get; set; }

    public DateOnly DueDate { get; set; }

    public decimal AmountUsd { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = null!;

    public DateOnly? PaymentDate { get; set; }

    public string Items { get; set; } = null!;

    public string? PdfUrl { get; set; }

    public string? StripeInvoiceId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Subscription Subscription { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

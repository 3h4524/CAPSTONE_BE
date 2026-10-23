using System;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueInvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = null!;
    /// <summary>The customer's user id; the table search matches it.</summary>
    public Guid UserId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerEmail { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal Amount { get; set; }
    /// <summary>Display status: paid, pending, overdue, refunded, cancelled or draft.</summary>
    public string Status { get; set; } = null!;
    /// <summary>Payment date, or the invoice date when it was never paid.</summary>
    public DateOnly? PaymentDate { get; set; }
    /// <summary>When the invoice was created (checkout time); used to show a time of day.</summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>The payment gateway ("PayOS", "Stripe"); null when none was involved. Card details are not stored.</summary>
    public string? PaymentMethod { get; set; }
}

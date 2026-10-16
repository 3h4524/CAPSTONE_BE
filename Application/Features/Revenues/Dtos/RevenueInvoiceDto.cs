using System;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueInvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string CustomerEmail { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly? PaymentDate { get; set; }
}

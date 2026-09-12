namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// One row in the Recent Invoices table (BR188).
/// </summary>
public sealed record RecentInvoiceDto(
    Guid InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string PlanName,
    decimal TotalAmount,
    string Status);

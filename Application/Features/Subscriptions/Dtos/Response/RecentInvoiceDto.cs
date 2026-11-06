namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// One row in the Recent Invoices table (BR188).
/// </summary>
/// <param name="TotalAmountVnd">The stored USD total in whole đồng, as PayOS charged it.</param>
public sealed record RecentInvoiceDto(
    Guid InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string PlanName,
    decimal TotalAmount,
    string Status,
    decimal TotalAmountVnd = 0m);

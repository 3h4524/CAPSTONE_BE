namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The current status of a checkout the Seller is waiting on, for the client to poll.
/// </summary>
/// <param name="Status">One of "pending", "paid", or "failed".</param>
/// <param name="PlanName">Populated once known; present even while pending.</param>
public sealed record CheckoutStatusResponseDto(
    string Status,
    string PlanName,
    string InvoiceNumber,
    DateOnly? RenewalDate);

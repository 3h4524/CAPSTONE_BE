namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// A newly created, unpaid PayOS checkout for the Seller to complete (UC59/UC60).
/// </summary>
/// <param name="QrCode">The raw VietQR payload string; the client renders this as a QR image.</param>
public sealed record CheckoutResponseDto(
    Guid InvoiceId,
    string QrCode,
    string CheckoutUrl,
    int AmountVnd);

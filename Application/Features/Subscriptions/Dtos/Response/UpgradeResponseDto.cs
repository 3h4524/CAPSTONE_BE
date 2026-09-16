namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The result of upgrading to a higher-tier plan (UC62).
/// </summary>
/// <param name="PaymentRequired">
/// True when <see cref="DueTodayUsd"/> is greater than zero and the client must complete UC60
/// (Process Payment) using the PayOS fields below before the upgrade activates (BR111). False
/// means the prorated credit fully covered the new plan's price and the upgrade already took
/// effect immediately.
/// </param>
/// <param name="DueTodayUsd">The net amount due today after the prorated credit (BR108).</param>
/// <param name="ProratedCreditUsd">The credit applied for the unused days of the current cycle.</param>
/// <param name="MonthlyRate">The target plan's price at the subscription's current billing cycle.</param>
/// <param name="FirstRenewalDate">Unchanged from the existing billing cycle (BR110).</param>
/// <param name="InvoiceId">The invoice created for this upgrade, paid or awaiting payment.</param>
/// <param name="QrCode">Populated only when <see cref="PaymentRequired"/> is true.</param>
/// <param name="CheckoutUrl">Populated only when <see cref="PaymentRequired"/> is true.</param>
/// <param name="AmountVnd">Populated only when <see cref="PaymentRequired"/> is true.</param>
public sealed record UpgradeResponseDto(
    bool PaymentRequired,
    decimal DueTodayUsd,
    decimal ProratedCreditUsd,
    decimal MonthlyRate,
    DateOnly FirstRenewalDate,
    Guid InvoiceId,
    string? QrCode,
    string? CheckoutUrl,
    int? AmountVnd);

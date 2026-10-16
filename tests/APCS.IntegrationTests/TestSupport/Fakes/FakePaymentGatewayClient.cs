using APCS.Application.Features.Subscriptions.Common;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Answers every payment call locally instead of creating or cancelling a real PayOS transaction.
/// </summary>
public sealed class FakePaymentGatewayClient : IPaymentGatewayClient
{
    /// <inheritdoc />
    public Task<PaymentLinkResult> CreatePaymentLinkAsync(
        long orderCode,
        int amountVnd,
        string description,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentLinkResult(
            $"test-link-{orderCode}",
            $"https://payments.test/checkout/{orderCode}",
            string.Empty));
    }

    /// <inheritdoc />
    public Task<WebhookVerificationResult> VerifyWebhookAsync(
        string rawJsonBody,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WebhookVerificationResult(IsValid: false, OrderCode: 0, Success: false));
    }

    /// <inheritdoc />
    public Task<PaymentLinkStatusResult> GetPaymentLinkStatusAsync(
        long orderCode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentLinkStatusResult(IsPaid: false, IsFailed: false));
    }

    /// <inheritdoc />
    public Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelPaymentLinkAsync(
        long orderCode,
        string cancellationReason,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

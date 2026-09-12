using System.Text.Json;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using PayOS;
using PayOS.Exceptions;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IPaymentGatewayClient"/> against the real PayOS API via the official
/// <c>payOS</c> SDK.
/// </summary>
/// <remarks>
/// PayOS has no sandbox: every call this class makes is a real payment. The <see cref="PayOsOptions"/>
/// keys come from the merchant's own PayOS payment channel (my.payos.vn).
/// </remarks>
public sealed class PayOsGatewayClient : IPaymentGatewayClient, IDisposable
{
    private readonly PayOSClient client;
    private readonly AppOptions appOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayOsGatewayClient"/> class.
    /// </summary>
    public PayOsGatewayClient(IOptions<PayOsOptions> options, IOptions<AppOptions> appOptions)
    {
        this.appOptions = appOptions.Value;
        var value = options.Value;
        client = new PayOSClient(new PayOSOptions
        {
            ClientId = value.ClientId,
            ApiKey = value.ApiKey,
            ChecksumKey = value.ChecksumKey
        });
    }

    /// <inheritdoc />
    public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
        long orderCode,
        int amountVnd,
        string description,
        CancellationToken cancellationToken = default)
    {
        // Redirects only matter if the Seller opens the hosted checkoutUrl separately; the
        // embedded-QR flow this app uses never navigates the Seller away in the first place.
        var redirectUrl = $"{appOptions.BaseUrl.TrimEnd('/')}/subscription";

        var request = new CreatePaymentLinkRequest
        {
            OrderCode = orderCode,
            Amount = amountVnd,
            Description = description,
            ReturnUrl = redirectUrl,
            CancelUrl = redirectUrl
        };

        // The SDK computes and attaches the request signature itself using the channel's
        // checksum key; this class never hand-rolls PayOS's HMAC scheme.
        var response = await client.PaymentRequests.CreateAsync(
            request,
            new RequestOptions<CreatePaymentLinkRequest> { CancellationToken = cancellationToken });

        return new PaymentLinkResult(response.PaymentLinkId, response.CheckoutUrl, response.QrCode);
    }

    /// <inheritdoc />
    public async Task<WebhookVerificationResult> VerifyWebhookAsync(
        string rawJsonBody,
        CancellationToken cancellationToken = default)
    {
        var webhook = JsonSerializer.Deserialize<Webhook>(rawJsonBody);
        if (webhook is null)
        {
            return new WebhookVerificationResult(false, 0, false);
        }

        try
        {
            // Throws when the signature does not match the channel's checksum key.
            var verified = await client.Webhooks.VerifyAsync(webhook);
            return new WebhookVerificationResult(true, verified.OrderCode, webhook.Success);
        }
        catch (PayOSException)
        {
            return new WebhookVerificationResult(false, 0, false);
        }
    }

    /// <inheritdoc />
    public async Task<PaymentLinkStatusResult> GetPaymentLinkStatusAsync(
        long orderCode,
        CancellationToken cancellationToken = default)
    {
        var link = await client.PaymentRequests.GetAsync(
            orderCode,
            new RequestOptions { CancellationToken = cancellationToken });

        var isFailed = link.Status is PaymentLinkStatus.Cancelled
            or PaymentLinkStatus.Expired
            or PaymentLinkStatus.Failed;

        return new PaymentLinkStatusResult(link.Status == PaymentLinkStatus.Paid, isFailed);
    }

    /// <inheritdoc />
    public async Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default) =>
        await client.Webhooks.ConfirmAsync(
            webhookUrl,
            new RequestOptions<PayOS.Models.Webhooks.ConfirmWebhookRequest> { CancellationToken = cancellationToken });

    /// <inheritdoc />
    public async Task CancelPaymentLinkAsync(
        long orderCode,
        string cancellationReason,
        CancellationToken cancellationToken = default) =>
        await client.PaymentRequests.CancelAsync(
            orderCode,
            cancellationReason,
            new RequestOptions<CancelPaymentLinkRequest> { CancellationToken = cancellationToken });

    /// <inheritdoc />
    public void Dispose() => client.Dispose();
}

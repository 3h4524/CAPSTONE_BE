using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using PayOS.Models.V2.PaymentRequests;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class PayOsGatewayClientTests
{
    [TestMethod]
    [DataRow("http://localhost:3000", "http://localhost:3000/subscription")]
    [DataRow("http://localhost:3000/", "http://localhost:3000/subscription")]
    [DataRow("https://app.example.com", "https://app.example.com/subscription")]
    public void BuildRedirectUrl_AlwaysPointsAtTheSubscriptionPage(string baseUrl, string expected) =>
        PayOsGatewayClient.BuildRedirectUrl(baseUrl).Should().Be(expected);

    [TestMethod]
    [DataRow(PaymentLinkStatus.Paid, true, false)]
    [DataRow(PaymentLinkStatus.Cancelled, false, true)]
    [DataRow(PaymentLinkStatus.Expired, false, true)]
    [DataRow(PaymentLinkStatus.Failed, false, true)]
    [DataRow(PaymentLinkStatus.Pending, false, false)]
    [DataRow(PaymentLinkStatus.Processing, false, false)]
    [DataRow(PaymentLinkStatus.Underpaid, false, false)]
    public void MapStatus_TranslatesEveryPayOsStatusToPaidOrFailedOrStillPending(
        PaymentLinkStatus status, bool expectedIsPaid, bool expectedIsFailed)
    {
        var result = PayOsGatewayClient.MapStatus(status);

        result.IsPaid.Should().Be(expectedIsPaid);
        result.IsFailed.Should().Be(expectedIsFailed);
    }

    [TestMethod]
    public void Constructor_WithConfiguredOptions_DoesNotThrow()
    {
        var act = () => CreateClient();

        act.Should().NotThrow();
    }

    [TestMethod]
    public async Task VerifyWebhookAsync_WhenBodyDeserializesToNull_ReturnsInvalidWithoutCallingPayOs()
    {
        using var client = CreateClient();

        var result = await client.VerifyWebhookAsync("null", CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.OrderCode.Should().Be(0);
        result.Success.Should().BeFalse();
    }

    [TestMethod]
    public async Task VerifyWebhookAsync_WhenSignatureDoesNotMatchTheChecksumKey_ReturnsInvalid()
    {
        // PayOS computes the HMAC over the payload's own "data" object using the channel's
        // checksum key entirely locally — no network call — so a mismatched signature (as here,
        // since the checksum key is a dummy test value) throws PayOS.Exceptions.WebhookException
        // synchronously, which the client is expected to translate into an invalid result rather
        // than let propagate.
        using var client = CreateClient();
        const string rawJsonBody = """
            {
              "code": "00",
              "desc": "success",
              "success": true,
              "data": {
                "orderCode": 123,
                "amount": 2000,
                "description": "Sub Starter",
                "accountNumber": "0123456789",
                "reference": "REF123",
                "transactionDateTime": "2026-01-01 00:00:00",
                "currency": "VND",
                "paymentLinkId": "abc123"
              },
              "signature": "0000000000000000000000000000000000000000000000000000000000000000"
            }
            """;

        var result = await client.VerifyWebhookAsync(rawJsonBody, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.OrderCode.Should().Be(0);
        result.Success.Should().BeFalse();
    }

    private static PayOsGatewayClient CreateClient() => new(
        Microsoft.Extensions.Options.Options.Create(new PayOsOptions
        {
            ClientId = "test-client-id",
            ApiKey = "test-api-key",
            ChecksumKey = "test-checksum-key",
        }),
        Microsoft.Extensions.Options.Options.Create(new AppOptions
        {
            BaseUrl = "http://localhost:3000",
            VerifyEmailPath = "/verify-email",
        }));
}

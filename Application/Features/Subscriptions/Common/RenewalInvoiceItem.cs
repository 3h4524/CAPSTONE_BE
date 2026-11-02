using System.Text.Json;

namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// The line a renewal invoice carries in its <c>items</c> JSON. It marks the invoice as a
/// renewal of its own subscription, and records the plan and billing cycle that were priced, so
/// the payment applies exactly what the Seller paid for even if a scheduled change moves meanwhile.
/// </summary>
/// <param name="FromPlanId">
/// The plan the subscription was on when the renewal was billed. A paid renewal can switch the
/// subscription to its scheduled plan in place, so this is the only record of the plan before it;
/// revenue history reads it. Null on renewals billed before it was recorded.
/// </param>
/// <param name="FromBillingCycle">The billing cycle the subscription was on when the renewal was billed.</param>
internal sealed record RenewalInvoiceItem(
    string Description,
    int AmountVnd,
    Guid PlanId,
    string BillingCycle,
    Guid? FromPlanId = null,
    string? FromBillingCycle = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool Renewal => true;

    public string ToItemsJson() => JsonSerializer.Serialize(new[] { this }, JsonOptions);

    /// <summary>Reads the renewal line from an invoice's items; null for any other invoice.</summary>
    public static RenewalInvoiceItem? TryRead(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(itemsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("renewal", out var renewal)
                    && renewal.ValueKind == JsonValueKind.True)
                {
                    return item.Deserialize<RenewalInvoiceItem>(JsonOptions);
                }
            }
        }
        catch (JsonException)
        {
            // Items written by older code or by hand are never renewals.
        }

        return null;
    }
}

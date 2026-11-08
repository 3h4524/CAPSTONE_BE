using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Shops.Dtos.Response;

namespace APCS.Application.Features.Shops.Common;

/// <summary>Turns raw Etsy shop data into the seller-facing insights; pure, so every rule is testable.</summary>
public static class ShopInsightsCalculator
{
    public const int WindowDays = 30;

    /// <summary>Etsy allows 13 tags; a listing using fewer leaves search terms unused.</summary>
    public const int MaxTags = 13;

    /// <summary>Below this many photos a listing shows too little of the product (Etsy allows 10).</summary>
    public const int MinPhotos = 5;

    public const int LowStockAtOrBelow = 3;

    public static readonly TimeSpan ExpiringWithin = TimeSpan.FromDays(14);

    /// <summary>A listing seen this often with no favorite is not converting interest.</summary>
    public const int ViewedNotFavoritedViews = 40;

    private static readonly string[] CanceledStatuses = ["canceled", "fully refunded"];

    public static IReadOnlyList<ShopHealthCheckDto> Health(EtsyShopDetails shop, EtsyShopSetup? setup)
    {
        var checks = new List<ShopHealthCheckDto>
        {
            new("active_listings", shop.ActiveListingCount > 0, "Has active listings",
                shop.ActiveListingCount > 0 ? $"{shop.ActiveListingCount} listing(s) are live." : "Nothing is for sale yet."),
            new("not_on_vacation", !shop.IsOnVacation, "Shop is open",
                shop.IsOnVacation ? "Vacation mode hides your listings from buyers." : "Buyers can order."),
            new("etsy_payments", shop.IsEtsyPaymentsOnboarded, "Etsy Payments set up",
                shop.IsEtsyPaymentsOnboarded ? "You can be paid." : "Finish Etsy Payments to receive money."),
            new("return_policy", shop.HasReturnPolicy, "Return policy set",
                shop.HasReturnPolicy ? "Buyers see your return terms." : "Buyers check return terms before they order."),
            new("announcement", shop.Announcement is not null, "Shop announcement",
                shop.Announcement is not null ? "Shown at the top of your shop." : "A short announcement tells buyers what you make."),
        };
        if (setup is not null)
        {
            checks.Add(new("shipping_profile", setup.ShippingProfileCount > 0, "Shipping profile",
                setup.ShippingProfileCount > 0 ? $"{setup.ShippingProfileCount} profile(s)." : "Required to publish physical listings, including APCS drafts."));
            checks.Add(new("production_partner", setup.ProductionPartnerCount > 0, "Production partner",
                setup.ProductionPartnerCount > 0 ? $"{setup.ProductionPartnerCount} partner(s)." : "Etsy requires one for print-on-demand items made by a partner."));
        }
        return checks;
    }

    public static ListingInsightsDto Listings(IReadOnlyDictionary<string, int> countsByState, IReadOnlyList<EtsyListing> recent, DateTimeOffset now)
    {
        var rows = recent.Select(listing => Row(listing, now)).ToArray();
        int Count(string issue) => rows.Count(row => row.Issues.Contains(issue));
        return new ListingInsightsDto(countsByState, rows,
            new ListingIssueSummaryDto(rows.Length, Count("low_stock"), Count("expiring"), Count("few_tags"), Count("few_photos"), Count("viewed_not_favorited")));
    }

    public static ListingRowDto Row(EtsyListing listing, DateTimeOffset now)
    {
        var issues = new List<string>();
        var active = listing.State == "active";
        if (active && listing.Quantity <= LowStockAtOrBelow) issues.Add("low_stock");
        if (active && listing.EndingAtUtc is { } ending && ending > now && ending - now <= ExpiringWithin) issues.Add("expiring");
        if (listing.Tags.Count < MaxTags) issues.Add("few_tags");
        if (listing.ImageCount < MinPhotos) issues.Add("few_photos");
        if (active && listing.Views >= ViewedNotFavoritedViews && listing.Favorers == 0) issues.Add("viewed_not_favorited");
        return new ListingRowDto(listing.ListingId, listing.Title, listing.Url, listing.ImageUrl, listing.State,
            listing.Price?.Amount, listing.Price?.Currency, listing.Quantity, listing.Views, listing.Favorers,
            listing.Tags.Count, listing.ImageCount, listing.Tags, listing.UpdatedAtUtc, listing.EndingAtUtc, issues);
    }

    /// <param name="receipts">Orders of the current and the previous window.</param>
    /// <param name="payments">Payments read for some paid orders of the current window, by receipt id.</param>
    public static OrderInsightsDto Orders(
        IReadOnlyList<EtsyReceipt> receipts,
        IReadOnlyDictionary<string, EtsyPayment> payments,
        string currency,
        DateTimeOffset now,
        bool truncated)
    {
        var windowStart = now.AddDays(-WindowDays);
        var previousStart = windowStart.AddDays(-WindowDays);
        var counted = receipts.Where(receipt => !CanceledStatuses.Contains(receipt.Status)).ToArray();
        var current = counted.Where(receipt => receipt.CreatedAtUtc > windowStart && receipt.CreatedAtUtc <= now).ToArray();
        var previous = counted.Where(receipt => receipt.CreatedAtUtc > previousStart && receipt.CreatedAtUtc <= windowStart).ToArray();
        decimal Total(IEnumerable<EtsyReceipt> rows) => rows.Sum(receipt => InCurrency(receipt.GrandTotal, currency));

        var revenue = Total(current);
        var daily = Enumerable.Range(0, WindowDays)
            .Select(offset => DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(offset - WindowDays + 1)))
            .Select(day =>
            {
                var orders = current.Where(receipt => DateOnly.FromDateTime(receipt.CreatedAtUtc.UtcDateTime) == day).ToArray();
                return new DailyOrdersDto(day, orders.Length, Total(orders));
            })
            .ToArray();

        // Paid and not shipped is what the seller must act on; past the expected date it is late.
        var awaiting = receipts.Where(receipt => receipt.IsPaid && !receipt.IsShipped && !CanceledStatuses.Contains(receipt.Status)).ToArray();
        var late = awaiting.Count(receipt => receipt.Items.Any(item => item.ExpectedShipAtUtc is { } expected && expected < now));

        var fulfilment = current.SelectMany(receipt => receipt.Items)
            .Where(item => item.PaidAtUtc is not null && item.ShippedAtUtc is not null && item.ShippedAtUtc >= item.PaidAtUtc)
            .Select(item => (item.ShippedAtUtc!.Value - item.PaidAtUtc!.Value).TotalHours)
            .ToArray();

        var topListings = current.SelectMany(receipt => receipt.Items)
            .GroupBy(item => item.ListingId ?? item.Title)
            .Select(group => new TopListingDto(group.First().ListingId, group.First().Title, group.Sum(item => item.Quantity),
                group.Sum(item => InCurrency(item.Price, currency) * item.Quantity)))
            .OrderByDescending(item => item.Revenue).ThenByDescending(item => item.Units)
            .Take(5)
            .ToArray();

        var countries = current.Where(receipt => !string.IsNullOrWhiteSpace(receipt.CountryIso))
            .GroupBy(receipt => receipt.CountryIso!)
            .Select(group => new CountryShareDto(group.Key, group.Count()))
            .OrderByDescending(item => item.Orders)
            .Take(5)
            .ToArray();

        var refundedRows = receipts.Where(receipt => receipt.CreatedAtUtc > windowStart && receipt.Refunded is { Amount: > 0 }).ToArray();
        var sampled = current.Where(receipt => payments.ContainsKey(receipt.ReceiptId)).Select(receipt => payments[receipt.ReceiptId]).ToArray();
        decimal? fees = sampled.Length == 0 ? null : sampled.Sum(payment => InCurrency(payment.Fees, currency));
        decimal? net = sampled.Length == 0 ? null : sampled.Sum(payment => InCurrency(payment.Net, currency));

        return new OrderInsightsDto(
            WindowDays,
            current.Length,
            previous.Length,
            revenue,
            Total(previous),
            current.Sum(receipt => receipt.Items.Sum(item => item.Quantity)),
            current.Length == 0 ? null : Math.Round(revenue / current.Length, 2),
            daily,
            awaiting.Length,
            late,
            fulfilment.Length == 0 ? null : Math.Round(fulfilment.Average(), 1),
            fulfilment.Length,
            topListings,
            countries,
            refundedRows.Sum(receipt => InCurrency(receipt.Refunded, currency)),
            refundedRows.Length,
            fees,
            net,
            sampled.Length,
            truncated);
    }

    public static ReviewInsightsDto Reviews(IReadOnlyList<EtsyReview> reviews)
    {
        var distribution = Enumerable.Range(1, 5).ToDictionary(star => star, star => reviews.Count(review => review.Rating == star));
        decimal? average = reviews.Count == 0 ? null : Math.Round((decimal)reviews.Average(review => review.Rating), 2);
        var recent = reviews.OrderByDescending(review => review.CreatedAtUtc).Take(5)
            .Select(review => new ReviewDto(review.Rating, review.Text, review.ListingId, review.CreatedAtUtc))
            .ToArray();
        return new ReviewInsightsDto(average, reviews.Count, distribution, recent);
    }

    /// <summary>Amounts in another currency are left out rather than added at a wrong rate.</summary>
    private static decimal InCurrency(EtsyMoney? money, string currency) =>
        money is not null && string.Equals(money.Currency, currency, StringComparison.OrdinalIgnoreCase) ? money.Amount : 0;
}

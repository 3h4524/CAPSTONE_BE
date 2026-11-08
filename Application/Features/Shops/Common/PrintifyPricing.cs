namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// What a sale on Etsy leaves the seller, estimated from Etsy's published US fees: a 6.5% transaction fee and a
/// 3% + $0.25 payment fee, both on the order total including shipping, plus the $0.20 listing fee.
/// Fees differ by country and change over time, so this is guidance shown next to a price, never a charge.
/// </summary>
public static class PrintifyPricing
{
    public const decimal TransactionRate = 0.065m;
    public const decimal PaymentRate = 0.03m;
    public const decimal PaymentFixed = 0.25m;
    public const decimal ListingFee = 0.20m;

    public static decimal EstimatedFees(decimal price, decimal shipping)
    {
        var total = price + shipping;
        return decimal.Round(total * (TransactionRate + PaymentRate) + PaymentFixed + ListingFee, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The buyer pays shipping and Printify charges the same shipping, so only the item price and cost count.</summary>
    public static decimal EstimatedProfit(decimal price, decimal cost, decimal shipping) =>
        decimal.Round(price - cost - EstimatedFees(price, shipping), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The price ending in .99 that leaves at least <paramref name="profit"/> after <paramref name="cost"/> and the
    /// estimated fees: solves price − cost − fees(price) = profit, then rounds up to the next whole dollar less a cent.
    /// </summary>
    public static decimal PriceForProfit(decimal cost, decimal shipping, decimal profit)
    {
        var rate = TransactionRate + PaymentRate;
        var exact = (cost + profit + shipping * rate + PaymentFixed + ListingFee) / (1 - rate);
        var price = Math.Ceiling(exact + 0.01m) - 0.01m;
        return Math.Max(price, Math.Ceiling(cost) + 0.99m);
    }

    public static long ToCents(decimal amount) => (long)decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;
}

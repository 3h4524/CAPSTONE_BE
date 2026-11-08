using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Shops.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class ShopInsightsCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static EtsyMoney Usd(decimal amount) => new(amount, "USD");

    private static EtsyListing Listing(string id, int quantity = 10, int tags = 13, int images = 6, int views = 5, int favorers = 1, DateTimeOffset? ending = null, string state = "active") =>
        new(id, $"Listing {id}", null, state, Usd(25), quantity, views, favorers,
            Enumerable.Range(0, tags).Select(index => $"tag{index}").ToArray(), images, null, null, null, ending ?? Now.AddDays(90));

    private static EtsyReceipt Receipt(string id, int daysAgo, decimal total, string status = "paid", bool paid = true, bool shipped = true,
        string country = "US", EtsyReceiptItem[]? items = null, EtsyMoney? refunded = null) =>
        new(id, status, paid, shipped, Now.AddDays(-daysAgo), country, Usd(total), refunded,
            items ?? [new EtsyReceiptItem("L1", "Tee", 1, Usd(total), Now.AddDays(-daysAgo), Now.AddDays(-daysAgo).AddHours(30), null)]);

    [TestMethod]
    public void Row_FlagsEachIssueOnItsOwnRule()
    {
        ShopInsightsCalculator.Row(Listing("ok"), Now).Issues.Should().BeEmpty();
        ShopInsightsCalculator.Row(Listing("a", quantity: 3), Now).Issues.Should().Equal("low_stock");
        ShopInsightsCalculator.Row(Listing("b", ending: Now.AddDays(10)), Now).Issues.Should().Equal("expiring");
        ShopInsightsCalculator.Row(Listing("c", tags: 9), Now).Issues.Should().Equal("few_tags");
        ShopInsightsCalculator.Row(Listing("d", images: 3), Now).Issues.Should().Equal("few_photos");
        ShopInsightsCalculator.Row(Listing("e", views: 40, favorers: 0), Now).Issues.Should().Equal("viewed_not_favorited");
    }

    [TestMethod]
    public void Row_StockAndExpiryOnlyMatterForActiveListings()
    {
        ShopInsightsCalculator.Row(Listing("draft", quantity: 0, ending: Now.AddDays(3), state: "draft"), Now).Issues.Should().BeEmpty();
    }

    [TestMethod]
    public void Orders_ComparesTheWindowWithThePreviousOneAndLeavesCanceledOut()
    {
        var receipts = new[]
        {
            Receipt("r1", 2, 30), Receipt("r2", 10, 20), Receipt("r3", 5, 99, status: "canceled"),
            Receipt("p1", 40, 15),
        };

        var orders = ShopInsightsCalculator.Orders(receipts, new Dictionary<string, EtsyPayment>(), "USD", Now, truncated: false);

        orders.Orders.Should().Be(2);
        orders.Revenue.Should().Be(50);
        orders.PreviousOrders.Should().Be(1);
        orders.PreviousRevenue.Should().Be(15);
        orders.AverageOrderValue.Should().Be(25);
        orders.Daily.Should().HaveCount(ShopInsightsCalculator.WindowDays);
        orders.Daily.Sum(day => day.Revenue).Should().Be(50);
        orders.AverageFulfillmentHours.Should().Be(30);
    }

    [TestMethod]
    public void Orders_CountsPaidUnshippedAndLateOnes()
    {
        var late = new EtsyReceiptItem("L1", "Tee", 1, Usd(20), Now.AddDays(-6), null, Now.AddDays(-1));
        var onTime = new EtsyReceiptItem("L2", "Mug", 1, Usd(20), Now.AddDays(-1), null, Now.AddDays(3));
        var receipts = new[]
        {
            Receipt("late", 6, 20, shipped: false, items: [late]),
            Receipt("ontime", 1, 20, shipped: false, items: [onTime]),
            Receipt("unpaid", 1, 20, paid: false, shipped: false),
        };

        var orders = ShopInsightsCalculator.Orders(receipts, new Dictionary<string, EtsyPayment>(), "USD", Now, false);

        orders.AwaitingShipment.Should().Be(2);
        orders.LateShipments.Should().Be(1);
    }

    [TestMethod]
    public void Orders_FeesAndNetComeOnlyFromThePaymentsRead()
    {
        var receipts = new[] { Receipt("r1", 1, 40), Receipt("r2", 2, 60) };
        var payments = new Dictionary<string, EtsyPayment> { ["r1"] = new(Usd(40), Usd(4.5m), Usd(35.5m)) };

        var orders = ShopInsightsCalculator.Orders(receipts, payments, "USD", Now, false);

        orders.Fees.Should().Be(4.5m);
        orders.Net.Should().Be(35.5m);
        orders.FeeSampleOrders.Should().Be(1);
    }

    [TestMethod]
    public void Orders_LeavesOtherCurrenciesOutInsteadOfAddingThem()
    {
        var euro = new EtsyReceipt("eu", "paid", true, true, Now.AddDays(-1), "DE", new EtsyMoney(100, "EUR"), null, []);
        ShopInsightsCalculator.Orders([Receipt("us", 1, 20), euro], new Dictionary<string, EtsyPayment>(), "USD", Now, false)
            .Revenue.Should().Be(20);
    }

    [TestMethod]
    public void Orders_RanksTopListingsByRevenue()
    {
        var receipts = new[]
        {
            Receipt("a", 1, 50, items: [new("L1", "Hoodie", 1, Usd(50), null, null, null)]),
            Receipt("b", 2, 60, items: [new("L2", "Tee", 3, Usd(20), null, null, null)]),
        };

        ShopInsightsCalculator.Orders(receipts, new Dictionary<string, EtsyPayment>(), "USD", Now, false)
            .TopListings.Select(item => (item.Title, item.Units, item.Revenue))
            .Should().Equal(("Tee", 3, 60m), ("Hoodie", 1, 50m));
    }

    [TestMethod]
    public void Reviews_BuildsTheStarDistribution()
    {
        var reviews = new[] { 5, 5, 4, 1 }.Select((rating, index) => new EtsyReview(rating, null, null, Now.AddDays(-index))).ToArray();

        var insights = ShopInsightsCalculator.Reviews(reviews);

        insights.Average.Should().Be(3.75m);
        insights.Distribution[5].Should().Be(2);
        insights.Distribution[2].Should().Be(0);
        insights.Recent.First().CreatedAtUtc.Should().Be(Now);
    }

    [TestMethod]
    public void Health_ReportsMissingSetup()
    {
        var shop = new EtsyShopDetails("1", "Shop", null, null, null, "USD", 0, 0, 0, 0, null, 0, false, null);

        var checks = ShopInsightsCalculator.Health(shop, new EtsyShopSetup(0, 1, 0));

        checks.Where(check => !check.Passed).Select(check => check.Code)
            .Should().BeEquivalentTo("active_listings", "etsy_payments", "return_policy", "announcement", "shipping_profile");
    }
}

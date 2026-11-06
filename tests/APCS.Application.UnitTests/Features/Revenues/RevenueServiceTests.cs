using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Application.Features.Subscriptions;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class RevenueServiceTests
{
    private readonly Mock<IRepository<Invoice>> _invoiceRepoMock = new();
    private readonly Mock<IRepository<Subscription>> _subscriptionRepoMock = new();
    private readonly Mock<IRepository<SubscriptionPlan>> _planRepoMock = new();
    private readonly Mock<IRepository<ApiUsageRecord>> _usageRecordRepoMock = new();
    private readonly RevenueService _sut;

    public RevenueServiceTests()
    {
        // Tests that care about subscriptions, plans or usage override these.
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription>().AsQueryable().BuildMock());
        _planRepoMock.Setup(r => r.Query()).Returns(new List<SubscriptionPlan>().AsQueryable().BuildMock());
        _usageRecordRepoMock.Setup(r => r.Query()).Returns(new List<ApiUsageRecord>().AsQueryable().BuildMock());
        // A rate of 1 keeps every figure in the units the fixtures are written in.
        _sut = CreateService(usdToVndRate: 1m);
    }

    private RevenueService CreateService(decimal usdToVndRate) => new(
        _invoiceRepoMock.Object,
        _subscriptionRepoMock.Object,
        _planRepoMock.Object,
        _usageRecordRepoMock.Object,
        EmptyRepository<DesignImage>(),
        EmptyRepository<MockupImage>(),
        EmptyRepository<PromoVideo>(),
        Options.Create(new PaymentGatewaySettings(usdToVndRate, null)));

    private static IRepository<T> EmptyRepository<T>() where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Query()).Returns(new List<T>().AsQueryable().BuildMock());
        return repository.Object;
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenInvoicesAndSubscriptionsExist_ReturnsCorrectCalculatedMetrics()
    {
        // Arrange
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();

        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var user3Id = Guid.NewGuid();
        var plan1 = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", MonthlyPriceUsd = 50m };
        var plan2 = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Basic", MonthlyPriceUsd = 30m };

        var sub1 = Paying(user1Id, plan1, renewalDate: new DateOnly(2026, 11, 5));
        var sub2 = Paying(user2Id, plan2, renewalDate: new DateOnly(2026, 11, 20));
        // Paid since January and cancelled in this period: a customer in the previous period.
        var sub3 = Paying(user3Id, plan1, renewalDate: new DateOnly(2026, 10, 1));
        sub3.Status = "cancelled";
        sub3.CancelledAt = new DateTime(2026, 10, 10);

        var invoices = new List<Invoice>
        {
            // Current period paid
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, SubscriptionId = sub1.Id, Status = "paid", AmountUsd = 100m, PaymentDate = new DateOnly(2026, 10, 5) },
            new Invoice { Id = Guid.NewGuid(), UserId = user2Id, SubscriptionId = sub2.Id, Status = "paid", AmountUsd = 200m, PaymentDate = new DateOnly(2026, 10, 20) },
            // Current period pending (ignored)
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, SubscriptionId = sub1.Id, Status = "pending", AmountUsd = 500m, PaymentDate = new DateOnly(2026, 10, 10) },
            // Previous period paid (31 days prior: 2026-08-31 to 2026-09-30)
            new Invoice { Id = Guid.NewGuid(), UserId = user2Id, SubscriptionId = sub2.Id, Status = "paid", AmountUsd = 150m, PaymentDate = new DateOnly(2026, 9, 15) },
            // Far in the past (ignored by revenue)
            new Invoice { Id = Guid.NewGuid(), UserId = user3Id, SubscriptionId = sub3.Id, Status = "paid", AmountUsd = 80m, PaymentDate = new DateOnly(2026, 1, 1) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);
        _planRepoMock.Setup(r => r.Query()).Returns(new List<SubscriptionPlan> { plan1, plan2 }.AsQueryable().BuildMock());

        var subscriptions = new List<Subscription>
        {
            sub1,
            sub2,
            sub3,
            // Never paid: not a subscriber.
            new Subscription { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "active", PlanId = plan1.Id, Plan = plan1, MonthlyPriceUsd = 50m, RenewalDate = new DateOnly(2026, 12, 1), CreatedAt = new DateTime(2026, 5, 1) },
            // A free trial is not a paying subscriber.
            new Subscription { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "active", PlanId = plan1.Id, Plan = plan1, CreatedAt = new DateTime(2026, 10, 20), TrialEndsAt = new DateTime(2026, 11, 3) }
        }.AsQueryable().BuildMock();

        _subscriptionRepoMock.Setup(r => r.Query()).Returns(subscriptions);

        // Act
        var result = await _sut.GetMetricsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value;
        metrics.Should().NotBeNull();

        // Current revenue: 100 + 200 = 300, previous: 150
        metrics.TotalNetRevenue.Value.Should().Be(300m);
        metrics.TotalNetRevenue.PreviousValue.Should().Be(150m);
        metrics.TotalNetRevenue.PercentageChange.Should().Be(100m); // ((300 - 150) / 150) * 100 = 100%

        // Active subscribers on Oct 31: user1 and user2. On Sep 30: user2 and user3, who has left
        // since but was paying then.
        metrics.ActiveSubscribers.Value.Should().Be(2m);
        metrics.ActiveSubscribers.PreviousValue.Should().Be(2m);
        metrics.ActiveSubscribers.PercentageChange.Should().Be(0m);

        // ARPU: current = 300 / 2 = 150, previous = 150 / 1 = 150
        metrics.Arpu.Value.Should().Be(150m);
        metrics.Arpu.PreviousValue.Should().Be(150m);
        metrics.Arpu.PercentageChange.Should().Be(0m);

        // MRR on Oct 31: 50 + 30 = 80. On Sep 30: Basic 30 + the cancelled Pro 50 = 80.
        metrics.SubscriptionMrr.Value.Should().Be(80m);
        metrics.SubscriptionMrr.PreviousValue.Should().Be(80m);
        metrics.Arr.Value.Should().Be(960m);
    }

    [TestMethod]
    public async Task GetMetricsAsync_ConvertsMoneyToVndAtThePaymentGatewayRate()
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Starter", MonthlyPriceUsd = 9.99m };
        var subscription = Paying(Guid.NewGuid(), plan, renewalDate: new DateOnly(2026, 11, 15));
        _invoiceRepoMock.Setup(r => r.Query()).Returns(new List<Invoice>
        {
            new() { Id = Guid.NewGuid(), UserId = subscription.UserId, SubscriptionId = subscription.Id, Status = "paid", AmountUsd = 9.99m, PaymentDate = new DateOnly(2026, 10, 15) }
        }.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription> { subscription }.AsQueryable().BuildMock());
        _planRepoMock.Setup(r => r.Query()).Returns(new List<SubscriptionPlan> { plan }.AsQueryable().BuildMock());
        var sut = CreateService(usdToVndRate: 25_000m);

        var result = await sut.GetMetricsAsync(new RevenueFilterDto { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });

        // 9.99 USD x 25,000 = 249,750 VND, what PayOS charged.
        result.Value.TotalRevenue.Value.Should().Be(249_750m);
        result.Value.SubscriptionMrr.Value.Should().Be(249_750m);
    }

    [TestMethod]
    public async Task GetMetricsAsync_AnAnnualSubscription_AddsATwelfthOfItsStoredPriceToMrr()
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", MonthlyPriceUsd = 30m, AnnualPriceUsd = 300m };
        var subscription = Paying(Guid.NewGuid(), plan, renewalDate: new DateOnly(2027, 10, 1));
        subscription.BillingCycle = "annual";
        subscription.AnnualPriceUsd = 240m; // bought before a price rise
        _invoiceRepoMock.Setup(r => r.Query()).Returns(new List<Invoice>
        {
            new() { Id = Guid.NewGuid(), UserId = subscription.UserId, SubscriptionId = subscription.Id, Status = "paid", AmountUsd = 240m, PaymentDate = new DateOnly(2026, 10, 1) }
        }.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription> { subscription }.AsQueryable().BuildMock());
        _planRepoMock.Setup(r => r.Query()).Returns(new List<SubscriptionPlan> { plan }.AsQueryable().BuildMock());

        var result = await _sut.GetMetricsAsync(new RevenueFilterDto { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });

        result.Value.SubscriptionMrr.Value.Should().Be(20m);
    }

    /// <summary>A monthly subscription first paid on Jan 1 2026 (its invoice is added by the test).</summary>
    private static Subscription Paying(Guid userId, SubscriptionPlan plan, DateOnly renewalDate) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PlanId = plan.Id,
        Plan = plan,
        Status = "active",
        BillingCycle = "monthly",
        MonthlyPriceUsd = plan.MonthlyPriceUsd,
        AnnualPriceUsd = plan.AnnualPriceUsd,
        StartDate = new DateOnly(2026, 1, 1),
        RenewalDate = renewalDate,
        CreatedAt = new DateTime(2026, 1, 1)
    };

    [TestMethod]
    public async Task GetMetricsAsync_WhenNoDataExists_HandlesZeroDivisionGracefully()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31)
        };

        _invoiceRepoMock.Setup(r => r.Query()).Returns(new List<Invoice>().AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetMetricsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalNetRevenue.Value.Should().Be(0m);
        result.Value.TotalNetRevenue.PercentageChange.Should().BeNull("there is no previous value to compare against");
        result.Value.ActiveSubscribers.Value.Should().Be(0m);
        result.Value.Arpu.Value.Should().Be(0m);
        result.Value.SubscriptionMrr.Value.Should().Be(0m);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenPreviousRevenueIsZeroAndCurrentIsPositive_LeavesPercentageChangeUndefined()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var invoices = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "paid", AmountUsd = 250m, PaymentDate = new DateOnly(2026, 10, 15) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetMetricsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalNetRevenue.PreviousValue.Should().Be(0m);
        result.Value.TotalNetRevenue.PercentageChange.Should().BeNull("growth from nothing is not +100%");
    }

    [TestMethod]
    public async Task GetMetricsAsync_WithARefundedInvoice_SubtractsTheRefundOnceFromGross()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var invoices = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "paid", TotalAmount = 300m, PaymentDate = new DateOnly(2026, 10, 5) },
            new Invoice { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = "refunded", TotalAmount = 100m, PaymentDate = new DateOnly(2026, 10, 6) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetMetricsAsync(filter, CancellationToken.None);

        // Assert
        result.Value.TotalRevenue.Value.Should().Be(400m, "gross includes the invoice that was later refunded");
        result.Value.RefundAmount.Value.Should().Be(100m);
        result.Value.NetRevenue.Value.Should().Be(300m, "net = gross - refunds, the refund subtracted exactly once");
    }

    [TestMethod]
    public async Task GetChartsAsync_WithMonthGranularity_ReturnsGroupedTrendAndByPlan()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Granularity = "Month"
        };

        var planPro = new SubscriptionPlan { Name = "Pro" };
        var planStarter = new SubscriptionPlan { Name = "Starter" };

        var invoices = new List<Invoice>
        {
            new Invoice
            {
                Id = Guid.NewGuid(),
                Status = "paid",
                AmountUsd = 300m,
                PaymentDate = new DateOnly(2026, 9, 10),
                Subscription = new Subscription { Plan = planPro }
            },
            new Invoice
            {
                Id = Guid.NewGuid(),
                Status = "paid",
                AmountUsd = 200m,
                PaymentDate = new DateOnly(2026, 10, 5),
                Subscription = new Subscription { Plan = planStarter }
            }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        // Act
        var result = await _sut.GetChartsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var chart = result.Value;
        chart.Should().NotBeNull();

        // Trend verification
        chart.Trend.Should().HaveCount(2);
        chart.Trend[0].DateLabel.Should().Be("2026-09");
        chart.Trend[0].Amount.Should().Be(300m);
        chart.Trend[1].DateLabel.Should().Be("2026-10");
        chart.Trend[1].Amount.Should().Be(200m);

        // ByPlan verification (Total = 500)
        chart.ByPlan.Should().HaveCount(2);
        chart.ByPlan[0].PlanName.Should().Be("Pro");
        chart.ByPlan[0].Amount.Should().Be(300m);
        chart.ByPlan[0].Percentage.Should().Be(60m);

        chart.ByPlan[1].PlanName.Should().Be("Starter");
        chart.ByPlan[1].Amount.Should().Be(200m);
        chart.ByPlan[1].Percentage.Should().Be(40m);

        // PlanBreakdown per trend point verification
        chart.Trend[0].PlanBreakdown.Should().ContainKey("Pro").WhoseValue.Should().Be(300m);
        chart.Trend[0].PlanBreakdown.Should().NotContainKey("Starter");
        chart.Trend[1].PlanBreakdown.Should().ContainKey("Starter").WhoseValue.Should().Be(200m);
        chart.Trend[1].PlanBreakdown.Should().NotContainKey("Pro");

        // AvailablePlans verification
        chart.AvailablePlans.Should().BeEquivalentTo(new[] { "Pro", "Starter" });
    }

    [TestMethod]
    public async Task GetChartsAsync_ByPlan_ReportsCustomersArpuAndRetentionAndMatchesTotalRevenue()
    {
        // Arrange: 6 Pro customers live at the start of October, 1 cancels mid-month;
        // 2 Basic customers new in October. Only Pro has enough history for retention.
        var filter = new RevenueFilterDto { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), Granularity = "Day" };
        var pro = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", MonthlyPriceUsd = 50m };
        var basic = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Basic", MonthlyPriceUsd = 20m };

        Subscription Sub(SubscriptionPlan plan, DateTime createdAt, DateTime? cancelledAt = null) => new()
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Plan = plan, Status = cancelledAt is null ? "active" : "cancelled",
            StartDate = DateOnly.FromDateTime(createdAt), CreatedAt = createdAt, CancelledAt = cancelledAt,
            MonthlyPriceUsd = plan.MonthlyPriceUsd, RenewalDate = new DateOnly(2026, 12, 1)
        };
        Invoice Paid(Subscription s, DateOnly on, decimal amount) => new()
        {
            Id = Guid.NewGuid(), UserId = s.UserId, SubscriptionId = s.Id, Subscription = s,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}", Status = "paid", TotalAmount = amount, PaymentDate = on
        };

        var proSubs = Enumerable.Range(0, 6).Select(_ => Sub(pro, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))).ToList();
        proSubs[5].CancelledAt = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var basicSubs = Enumerable.Range(0, 2).Select(_ => Sub(basic, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc))).ToList();

        var invoices = proSubs.Select(s => Paid(s, new DateOnly(2026, 9, 1), 50m))
            .Concat(proSubs.Take(3).Select(s => Paid(s, new DateOnly(2026, 10, 1), 50m)))
            .Append(Paid(proSubs[0], new DateOnly(2026, 10, 20), 50m))
            .Concat(basicSubs.Select(s => Paid(s, new DateOnly(2026, 10, 3), 20m)))
            .ToList();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(proSubs.Concat(basicSubs).AsQueryable().BuildMock());

        // Act
        var chart = (await _sut.GetChartsAsync(filter, CancellationToken.None)).Value;
        var metrics = (await _sut.GetMetricsAsync(filter, CancellationToken.None)).Value;

        // Assert
        var proRow = chart.ByPlan.Single(p => p.PlanName == "Pro");
        proRow.Amount.Should().Be(200m);
        proRow.PurchaseCount.Should().Be(4);
        proRow.UniqueCustomers.Should().Be(3);
        proRow.Arpu.Should().Be(66.67m);
        proRow.Customers.Should().BeEquivalentTo(new PlanCustomerFlowDto { Start = 6, Stayed = 5, StoppedPaying = 1, End = 5 });
        proRow.RetentionRate.Should().Be(83.3m, "5 of the 6 Pro customers paying on Sep 30 are still paying on Oct 31");

        var basicRow = chart.ByPlan.Single(p => p.PlanName == "Basic");
        basicRow.UniqueCustomers.Should().Be(2);
        basicRow.Arpu.Should().Be(20m);
        basicRow.Customers.Should().BeEquivalentTo(new PlanCustomerFlowDto { New = 2, End = 2 });
        basicRow.RetentionRate.Should().BeNull("Basic had no customers at the start of the period");
        basicRow.PercentageChange.Should().BeNull();

        chart.ByPlan.Sum(p => p.Amount).Should().Be(metrics.TotalRevenue.Value, "the plan rows must add up to the Total Revenue KPI");
    }

    [TestMethod]
    public async Task GetChartsAsync_WithYearGranularity_ReturnsGroupedTrendByYear()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Granularity = "Year"
        };

        var invoices = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), Status = "paid", AmountUsd = 1000m, PaymentDate = new DateOnly(2025, 5, 1) },
            new Invoice { Id = Guid.NewGuid(), Status = "paid", AmountUsd = 2000m, PaymentDate = new DateOnly(2026, 6, 1) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        // Act
        var result = await _sut.GetChartsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Trend.Should().HaveCount(2);
        result.Value.Trend[0].DateLabel.Should().Be("2025");
        result.Value.Trend[0].Amount.Should().Be(1000m);
        result.Value.Trend[1].DateLabel.Should().Be("2026");
        result.Value.Trend[1].Amount.Should().Be(2000m);
    }

    [TestMethod]
    public async Task GetChartsAsync_WithDayGranularity_ReturnsGroupedTrendByDay()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 3),
            Granularity = "Day"
        };

        var invoices = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), Status = "paid", AmountUsd = 50m, PaymentDate = new DateOnly(2026, 10, 1) },
            new Invoice { Id = Guid.NewGuid(), Status = "paid", AmountUsd = 70m, PaymentDate = new DateOnly(2026, 10, 2) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        // Act
        var result = await _sut.GetChartsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Trend.Should().HaveCount(2);
        result.Value.Trend[0].DateLabel.Should().Be("2026-10-01");
        result.Value.Trend[0].Amount.Should().Be(50m);
        result.Value.Trend[1].DateLabel.Should().Be("2026-10-02");
        result.Value.Trend[1].Amount.Should().Be(70m);
    }

    [TestMethod]
    public async Task GetChartsAsync_WhenNoInvoices_HandlesEmptyGracefully()
    {
        // Arrange
        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31),
            Granularity = "Month"
        };

        _invoiceRepoMock.Setup(r => r.Query()).Returns(new List<Invoice>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetChartsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Trend.Should().BeEmpty();
        result.Value.ByPlan.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetInvoicesAsync_WithPagination_ReturnsCorrectPagedItems()
    {
        // Arrange
        var user = new User { FullName = "John Doe", Email = "john@example.com" };
        var plan = new SubscriptionPlan { Name = "Enterprise" };
        var sub = new Subscription { Plan = plan };

        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Page = 1,
            PageSize = 2,
            Status = "paid"
        };

        var invoices = new List<Invoice>
        {
            new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-001",
                Status = "paid",
                AmountUsd = 100m,
                PaymentDate = new DateOnly(2026, 10, 20),
                User = user,
                Subscription = sub
            },
            new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-002",
                Status = "paid",
                AmountUsd = 200m,
                PaymentDate = new DateOnly(2026, 10, 15),
                User = user,
                Subscription = sub
            },
            new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-003",
                Status = "paid",
                AmountUsd = 300m,
                PaymentDate = new DateOnly(2026, 10, 10),
                User = user,
                Subscription = sub
            },
            // Excluded by the "paid" status filter
            new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-004",
                Status = "unpaid",
                AmountUsd = 400m,
                PaymentDate = new DateOnly(2026, 10, 10),
                User = user,
                Subscription = sub
            }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        // Act
        var result = await _sut.GetInvoicesAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var paged = result.Value;
        paged.TotalCount.Should().Be(3);
        paged.Items.Should().HaveCount(2);

        // Check descending paymentDate ordering
        paged.Items[0].InvoiceNumber.Should().Be("INV-001");
        paged.Items[0].CustomerName.Should().Be("John Doe");
        paged.Items[0].CustomerEmail.Should().Be("john@example.com");
        paged.Items[0].PlanName.Should().Be("Enterprise");
        paged.Items[0].Amount.Should().Be(100m);
        paged.Items[0].Status.Should().Be("paid");

        paged.Items[1].InvoiceNumber.Should().Be("INV-002");
    }

    [TestMethod]
    public async Task GetInvoicesAsync_ListsEveryStatusWithDisplayNamesAndAppliesSearchPlanAndStatusFilters()
    {
        // Arrange
        var alice = new User { Id = Guid.NewGuid(), FullName = "Alice Nguyen", Email = "alice@shop.vn" };
        var bob = new User { Id = Guid.Parse("0b0b0b0b-1234-4abc-9def-00000000b0b0"), FullName = "Bob Tran", Email = "bob@shop.vn" };
        var creator = new Subscription { Plan = new SubscriptionPlan { Name = "Creator" } };
        var studio = new Subscription { Plan = new SubscriptionPlan { Name = "Studio" } };

        Invoice Inv(string number, string status, User user, Subscription sub, long? payosOrder = null) => new()
        {
            Id = Guid.NewGuid(), InvoiceNumber = number, Status = status, TotalAmount = 79.99m,
            InvoiceDate = new DateOnly(2026, 10, 5), PaymentDate = status == "paid" ? new DateOnly(2026, 10, 5) : null,
            CreatedAt = new DateTime(2026, 10, 5, 4, 20, 0, DateTimeKind.Utc),
            UserId = user.Id, User = user, Subscription = sub, PayosOrderCode = payosOrder
        };

        var invoices = new List<Invoice>
        {
            Inv("INV-A1", "paid", alice, creator, payosOrder: 123),
            Inv("INV-A2", "issued", alice, studio),
            Inv("INV-B1", "void", bob, creator),
            Inv("INV-B2", "refunded", bob, studio)
        };
        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices.AsQueryable().BuildMock());

        RevenueFilterDto Filter(string? search = null, string? plan = null, string? status = null) => new()
        {
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
            Page = 1, PageSize = 10, Search = search, Plan = plan, Status = status
        };

        // Act
        var all = (await _sut.GetInvoicesAsync(Filter(), CancellationToken.None)).Value;
        var bySearch = (await _sut.GetInvoicesAsync(Filter(search: "ALICE"), CancellationToken.None)).Value;
        var byUserId = (await _sut.GetInvoicesAsync(Filter(search: bob.Id.ToString().ToUpperInvariant()), CancellationToken.None)).Value;
        var byUserIdPart = (await _sut.GetInvoicesAsync(Filter(search: "1234-4abc"), CancellationToken.None)).Value;
        var byPlan = (await _sut.GetInvoicesAsync(Filter(plan: "Studio"), CancellationToken.None)).Value;
        var pending = (await _sut.GetInvoicesAsync(Filter(status: "pending"), CancellationToken.None)).Value;
        var cancelled = (await _sut.GetInvoicesAsync(Filter(status: "cancelled"), CancellationToken.None)).Value;

        // Assert
        all.TotalCount.Should().Be(4);
        all.Items.Select(i => i.Status).Should().BeEquivalentTo(["paid", "pending", "cancelled", "refunded"]);

        var paid = all.Items.Single(i => i.InvoiceNumber == "INV-A1");
        paid.PaymentMethod.Should().Be("PayOS");
        paid.CreatedAt.Should().Be(new DateTime(2026, 10, 5, 4, 20, 0, DateTimeKind.Utc));
        all.Items.Single(i => i.InvoiceNumber == "INV-A2").PaymentMethod.Should().BeNull("no gateway was involved");

        bySearch.Items.Select(i => i.InvoiceNumber).Should().BeEquivalentTo(["INV-A1", "INV-A2"]);
        byUserId.Items.Select(i => i.InvoiceNumber).Should().BeEquivalentTo(["INV-B1", "INV-B2"], "a full user id matches whatever its case");
        byUserIdPart.Items.Select(i => i.InvoiceNumber).Should().BeEquivalentTo(["INV-B1", "INV-B2"], "a fragment of a user id matches too");
        paid.UserId.Should().Be(alice.Id);
        byPlan.Items.Select(i => i.InvoiceNumber).Should().BeEquivalentTo(["INV-A2", "INV-B2"]);
        pending.Items.Should().ContainSingle(i => i.InvoiceNumber == "INV-A2");
        cancelled.Items.Should().ContainSingle(i => i.InvoiceNumber == "INV-B1");
    }

    [TestMethod]
    public async Task GetInvoicesAsync_PageTwo_ReturnsSecondPageItems()
    {
        // Arrange
        var user = new User { FullName = "Jane Doe", Email = "jane@example.com" };
        var plan = new SubscriptionPlan { Name = "Basic" };
        var sub = new Subscription { Plan = plan };

        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Page = 2,
            PageSize = 2
        };

        var invoices = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), InvoiceNumber = "INV-001", Status = "paid", AmountUsd = 100m, PaymentDate = new DateOnly(2026, 10, 20), User = user, Subscription = sub },
            new Invoice { Id = Guid.NewGuid(), InvoiceNumber = "INV-002", Status = "paid", AmountUsd = 200m, PaymentDate = new DateOnly(2026, 10, 15), User = user, Subscription = sub },
            new Invoice { Id = Guid.NewGuid(), InvoiceNumber = "INV-003", Status = "paid", AmountUsd = 300m, PaymentDate = new DateOnly(2026, 10, 10), User = user, Subscription = sub }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        // Act
        var result = await _sut.GetInvoicesAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].InvoiceNumber.Should().Be("INV-003");
    }

    [TestMethod]
    public async Task GetInsightsAsync_ReturnsSubscriptionAnalyticsForecastAndAlerts()
    {
        // Arrange
        var plan = new SubscriptionPlan { Name = "Pro", MonthlyPriceUsd = 50m };
        var sub1 = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = "active",
            Plan = plan,
            StartDate = new DateOnly(2026, 10, 5),
            CreatedAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            RenewalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)
        };

        var filter = new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var invoices = new List<Invoice>
        {
            new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-101",
                Status = "paid",
                AmountUsd = 50m,
                PaymentDate = new DateOnly(2026, 10, 5),
                SubscriptionId = sub1.Id,
                Subscription = sub1
            }
        }.AsQueryable().BuildMock();

        var subscriptions = new List<Subscription> { sub1 }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(subscriptions);

        // Act
        var result = await _sut.GetInsightsAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.SubscriptionAnalytics.Should().NotBeNull();
        result.Value.SubscriptionAnalytics.NewSubscriptions.Value.Should().Be(1);
        result.Value.Forecast.Should().NotBeNull();
        result.Value.Forecast.Points.Should().NotBeEmpty();
        result.Value.FinancialAlerts.Should().NotBeNull();
        result.Value.FinancialAlerts.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task GetInsightsAsync_SubscriptionAnalytics_IgnoresUnpaidCheckoutsAndUsesActiveAtPeriodStart()
    {
        // Arrange
        var filter = new RevenueFilterDto { StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30) };
        var plan = new SubscriptionPlan { Name = "Pro", MonthlyPriceUsd = 50m };

        Subscription Sub(string status, DateTime createdAt, DateTime? cancelledAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = status,
            Plan = plan,
            StartDate = DateOnly.FromDateTime(createdAt),
            CreatedAt = createdAt,
            CancelledAt = cancelledAt
        };

        var loyal = Sub("active", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var churner = Sub("cancelled", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        var abandoned = Enumerable.Range(1, 6)
            .Select(d => Sub("cancelled", new DateTime(2026, 9, d, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, d, 1, 0, 0, DateTimeKind.Utc)))
            .ToList();

        Invoice Paid(Subscription s, DateOnly on) => new()
        {
            Id = Guid.NewGuid(), UserId = s.UserId, SubscriptionId = s.Id, Subscription = s,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}", Status = "paid", TotalAmount = 50m, PaymentDate = on
        };

        var invoices = new List<Invoice> { Paid(loyal, new DateOnly(2026, 8, 1)), Paid(churner, new DateOnly(2026, 8, 2)) }
            .Concat(abandoned.Select(s => new Invoice
            {
                Id = Guid.NewGuid(), UserId = s.UserId, SubscriptionId = s.Id, Subscription = s,
                InvoiceNumber = $"INV-{Guid.NewGuid():N}", Status = "void", TotalAmount = 50m, InvoiceDate = DateOnly.FromDateTime(s.CreatedAt!.Value)
            }))
            .ToList();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription> { loyal, churner }.Concat(abandoned).AsQueryable().BuildMock());

        // Act
        var analytics = (await _sut.GetInsightsAsync(filter, CancellationToken.None)).Value.SubscriptionAnalytics;

        // Assert
        analytics.NewSubscriptions.Value.Should().Be(0, "none of the six September checkouts was paid");
        analytics.Cancellations.Value.Should().Be(1, "only the paid subscription's cancellation is churn");
        analytics.ActiveAtPeriodStart.Should().Be(2);
        analytics.ChurnRate.Value.Should().Be(50m, "1 churned / 2 active at the start of September");
        analytics.HasChurnRate.Should().BeTrue();
        analytics.ChurnRate.PercentageChange.Should().Be(50m, "churn is compared in percentage points: 50% now vs 0% in August");
        analytics.Points.Sum(p => p.Cancellations).Should().Be(1);
    }

    [TestMethod]
    public async Task GetInsightsAsync_Forecast_GroupsPaidRevenueByCompletedWeekAndExcludesTheCurrentWeek()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var invoices = Enumerable.Range(1, 16)
            .Select(weeksAgo => new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = $"INV-W{weeksAgo}",
                Status = "paid",
                TotalAmount = 1000m * (17 - weeksAgo),
                PaymentDate = currentWeekStart.AddDays(-7 * weeksAgo + 2)
            })
            .Append(new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = "INV-CURRENT",
                Status = "paid",
                TotalAmount = 999_999m,
                PaymentDate = currentWeekStart
            })
            .ToList();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(new List<Subscription>().AsQueryable().BuildMock());

        var filter = new RevenueFilterDto { StartDate = today.AddDays(-30), EndDate = today };

        // Act
        var result = await _sut.GetInsightsAsync(filter, CancellationToken.None);

        // Assert
        var forecast = result.Value.Forecast;
        forecast.Status.Should().Be("ok");
        forecast.SampleWeeks.Should().Be(16);
        forecast.RSquared.Should().Be(1m);
        forecast.WeeklyTrend.Should().Be(1000m);

        var historical = forecast.Points.Where(p => p.IsHistorical).ToList();
        historical.Should().HaveCount(16);
        historical.Select(p => p.Actual).Should().NotContain(999_999m, "the unfinished current week is excluded");
        historical[^1].Actual.Should().Be(16_000m);
        historical[^1].ForecastBase.Should().Be(16_000m, "the forecast lines are anchored on the last observed week");

        var future = forecast.Points.Where(p => !p.IsHistorical).ToList();
        future.Should().HaveCount(52);
        future[0].ForecastBase.Should().Be(17_000m);
        future[0].Actual.Should().BeNull();
    }

    [TestMethod]
    public async Task GetChartsAsync_ByPlan_ReportsMrrChangeAndCustomersAndListsAPlanThatOnlyLostMrr()
    {
        var starter = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Starter", MonthlyPriceUsd = 10m };
        var pro = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", MonthlyPriceUsd = 30m };
        var staying = Paying(Guid.NewGuid(), starter, renewalDate: new DateOnly(2026, 11, 1));
        var leaving = Paying(Guid.NewGuid(), pro, renewalDate: new DateOnly(2026, 10, 1));
        leaving.Status = "cancelled";
        leaving.CancelledAt = new DateTime(2026, 9, 20);
        SetUp(
            [starter, pro],
            [staying, leaving],
            [
                PaidInvoice(staying, 10m, new DateOnly(2026, 8, 1)),
                PaidInvoice(staying, 10m, new DateOnly(2026, 9, 1)),
                PaidInvoice(leaving, 30m, new DateOnly(2026, 8, 1))
            ]);

        var result = await _sut.GetChartsAsync(new RevenueFilterDto
        {
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            Granularity = "Month"
        });

        var byPlan = result.Value.ByPlan.ToDictionary(p => p.PlanName);
        byPlan["Starter"].MrrStart.Should().Be(10m);
        byPlan["Starter"].MrrEnd.Should().Be(10m);
        byPlan["Starter"].Customers.Should().BeEquivalentTo(new PlanCustomerFlowDto { Start = 1, Stayed = 1, End = 1 });
        byPlan["Pro"].Amount.Should().Be(0m, "Pro collected nothing in September");
        byPlan["Pro"].MrrStart.Should().Be(30m);
        byPlan["Pro"].MrrEnd.Should().Be(0m);
        byPlan["Pro"].MrrChangePercent.Should().Be(-100m);
        byPlan["Pro"].Customers.Should().BeEquivalentTo(new PlanCustomerFlowDto { Start = 1, StoppedPaying = 1, End = 0 });
        byPlan["Pro"].UsageSignal.Left.Should().Be(1);
        byPlan["Pro"].UsageSignal.HasSignal.Should().BeFalse("one leaver is too few to compare");
    }

    [TestMethod]
    public async Task GetInsightsAsync_MrrMovement_BridgesThePeriodPerPlanAndListsSixMonths()
    {
        var starter = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Starter", MonthlyPriceUsd = 10m };
        var pro = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", MonthlyPriceUsd = 30m };
        var upgradedFrom = Paying(Guid.NewGuid(), starter, renewalDate: new DateOnly(2026, 10, 1));
        upgradedFrom.Status = "cancelled";
        upgradedFrom.CancelledAt = new DateTime(2026, 9, 10);
        var upgradedTo = Paying(upgradedFrom.UserId, pro, renewalDate: new DateOnly(2026, 10, 10));
        var newcomer = Paying(Guid.NewGuid(), starter, renewalDate: new DateOnly(2026, 10, 15));
        SetUp(
            [starter, pro],
            [upgradedFrom, upgradedTo, newcomer],
            [
                PaidInvoice(upgradedFrom, 10m, new DateOnly(2026, 8, 1)),
                PaidInvoice(upgradedTo, 20m, new DateOnly(2026, 9, 10)),
                PaidInvoice(newcomer, 10m, new DateOnly(2026, 9, 15))
            ]);

        var result = await _sut.GetInsightsAsync(new RevenueFilterDto { StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30) });

        var movement = result.Value.MrrMovement;
        movement.Months.Select(m => m.MonthStart).Should().Equal("2026-04-01", "2026-05-01", "2026-06-01", "2026-07-01", "2026-08-01", "2026-09-01");
        movement.Months[^1].New.Should().Be(10m);
        movement.Months[^1].Expansion.Should().Be(20m);
        movement.Months[^1].Net.Should().Be(30m);

        var all = movement.Period.Single(p => p.PlanName == null);
        all.Start.Should().Be(10m);
        all.End.Should().Be(40m);
        var proBridge = movement.Period.Single(p => p.PlanName == "Pro");
        proBridge.MovedIn.Should().Be(30m);
        proBridge.New.Should().Be(0m);
        var starterBridge = movement.Period.Single(p => p.PlanName == "Starter");
        starterBridge.MovedOut.Should().Be(10m);
        starterBridge.New.Should().Be(10m);
        starterBridge.End.Should().Be(10m);
    }

    private void SetUp(List<SubscriptionPlan> plans, List<Subscription> subscriptions, List<Invoice> invoices)
    {
        _planRepoMock.Setup(r => r.Query()).Returns(plans.AsQueryable().BuildMock());
        _subscriptionRepoMock.Setup(r => r.Query()).Returns(subscriptions.AsQueryable().BuildMock());
        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices.AsQueryable().BuildMock());
    }

    private static Invoice PaidInvoice(Subscription subscription, decimal amount, DateOnly paidOn) => new()
    {
        Id = Guid.NewGuid(),
        UserId = subscription.UserId,
        SubscriptionId = subscription.Id,
        Subscription = subscription,
        Status = "paid",
        AmountUsd = amount,
        TotalAmount = amount,
        InvoiceDate = paidOn,
        PaymentDate = paidOn
    };
}

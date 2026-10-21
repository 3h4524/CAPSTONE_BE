using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class RevenueServiceTests
{
    private readonly Mock<IRepository<Invoice>> _invoiceRepoMock = new();
    private readonly Mock<IRepository<Subscription>> _subscriptionRepoMock = new();
    private readonly RevenueService _sut;

    public RevenueServiceTests()
    {
        _sut = new RevenueService(_invoiceRepoMock.Object, _subscriptionRepoMock.Object);
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

        var plan1 = new SubscriptionPlan { Name = "Pro", MonthlyPriceUsd = 50m };
        var plan2 = new SubscriptionPlan { Name = "Basic", MonthlyPriceUsd = 30m };

        var invoices = new List<Invoice>
        {
            // Current period paid
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, Status = "paid", AmountUsd = 100m, PaymentDate = new DateOnly(2026, 10, 5) },
            new Invoice { Id = Guid.NewGuid(), UserId = user2Id, Status = "paid", AmountUsd = 200m, PaymentDate = new DateOnly(2026, 10, 20) },
            // Current period pending (ignored)
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, Status = "pending", AmountUsd = 500m, PaymentDate = new DateOnly(2026, 10, 10) },
            // Previous period paid (31 days prior: 2026-08-31 to 2026-09-30)
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, Status = "paid", AmountUsd = 150m, PaymentDate = new DateOnly(2026, 9, 15) },
            // Far in the past (ignored)
            new Invoice { Id = Guid.NewGuid(), UserId = user1Id, Status = "paid", AmountUsd = 80m, PaymentDate = new DateOnly(2026, 1, 1) }
        }.AsQueryable().BuildMock();

        _invoiceRepoMock.Setup(r => r.Query()).Returns(invoices);

        var subscriptions = new List<Subscription>
        {
            new Subscription { Id = Guid.NewGuid(), Status = "active", Plan = plan1, CreatedAt = new DateTime(2026, 10, 2) },
            new Subscription { Id = Guid.NewGuid(), Status = "active", Plan = plan2, CreatedAt = new DateTime(2026, 8, 1) },
            new Subscription { Id = Guid.NewGuid(), Status = "cancelled", Plan = plan1, CreatedAt = new DateTime(2026, 5, 1) }
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

        // Active subscribers (distinct userIds): current = 2, previous = 1
        metrics.ActiveSubscribers.Value.Should().Be(2m);
        metrics.ActiveSubscribers.PreviousValue.Should().Be(1m);
        metrics.ActiveSubscribers.PercentageChange.Should().Be(100m);

        // ARPU: current = 300 / 2 = 150, previous = 150 / 1 = 150
        metrics.Arpu.Value.Should().Be(150m);
        metrics.Arpu.PreviousValue.Should().Be(150m);
        metrics.Arpu.PercentageChange.Should().Be(0m);

        // MRR: active = 50 + 30 = 80, previous active (< previousEndDate): 30
        metrics.SubscriptionMrr.Value.Should().Be(80m);
        metrics.SubscriptionMrr.PreviousValue.Should().Be(30m);
    }

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
        result.Value.TotalNetRevenue.PercentageChange.Should().Be(0m);
        result.Value.ActiveSubscribers.Value.Should().Be(0m);
        result.Value.Arpu.Value.Should().Be(0m);
        result.Value.SubscriptionMrr.Value.Should().Be(0m);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenPreviousRevenueIsZeroAndCurrentIsPositive_SetsPercentageChangeTo100()
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
        result.Value.TotalNetRevenue.PercentageChange.Should().Be(100m);
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
            PageSize = 2
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
            // Unpaid invoice should be excluded
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
}

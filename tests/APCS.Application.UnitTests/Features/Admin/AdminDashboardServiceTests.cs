using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminDashboardServiceTests
{
    private static AdminDashboardService CreateService(
        Mock<IRepository<User>>? userRepository = null,
        Mock<IRepository<Subscription>>? subscriptionRepository = null,
        Mock<IRepository<Invoice>>? invoiceRepository = null,
        Mock<IRepository<BatchJob>>? batchJobRepository = null,
        Mock<IRepository<SupportTicket>>? ticketRepository = null,
        TimeProvider? timeProvider = null)
    {
        return new AdminDashboardService(
            (userRepository ?? new Mock<IRepository<User>>()).Object,
            (subscriptionRepository ?? new Mock<IRepository<Subscription>>()).Object,
            (invoiceRepository ?? new Mock<IRepository<Invoice>>()).Object,
            (batchJobRepository ?? new Mock<IRepository<BatchJob>>()).Object,
            (ticketRepository ?? new Mock<IRepository<SupportTicket>>()).Object,
            timeProvider ?? TimeProvider.System,
            NullLogger<AdminDashboardService>.Instance
        );
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenCalled_ReturnsCalculatedMetrics()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2023, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var now = timeProvider.GetUtcNow();
        var firstDayOfMonth = new DateOnly(now.Year, now.Month, 1);
        var lastDayOfMonth = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));

        var users = new List<User> { new User(), new User() }.AsQueryable().BuildMock();
        var userRepo = new Mock<IRepository<User>>();
        userRepo.Setup(r => r.Query()).Returns(users);

        var subscriptions = new List<Subscription>
        {
            new Subscription { UserId = Guid.NewGuid(), Status = "active", MonthlyPriceUsd = 15.0m },
            new Subscription { UserId = Guid.NewGuid(), Status = "active", MonthlyPriceUsd = 25.0m },
            new Subscription { UserId = Guid.NewGuid(), Status = "cancelled", MonthlyPriceUsd = 10.0m }
        }.AsQueryable().BuildMock();
        var subRepo = new Mock<IRepository<Subscription>>();
        subRepo.Setup(r => r.Query()).Returns(subscriptions);

        var invoices = new List<Invoice>
        {
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 10, 5), TotalAmount = 50.0m },
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 10, 20), TotalAmount = 30.0m },
            new Invoice { Status = "unpaid", InvoiceDate = new DateOnly(2023, 10, 21), TotalAmount = 100.0m },
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 9, 21), TotalAmount = 100.0m } // Outside month
        }.AsQueryable().BuildMock();
        var invRepo = new Mock<IRepository<Invoice>>();
        invRepo.Setup(r => r.Query()).Returns(invoices);

        var jobs = new List<BatchJob>
        {
            new BatchJob { Status = "running", User = new User { FullName = "Test User" } },
            new BatchJob { Status = "queued", User = new User { FullName = "Test User" } },
            new BatchJob { Status = "completed", User = new User { FullName = "Test User" } }
        }.AsQueryable().BuildMock();
        var jobRepo = new Mock<IRepository<BatchJob>>();
        jobRepo.Setup(r => r.Query()).Returns(jobs);

        var tickets = new List<SupportTicket>
        {
            new SupportTicket { Status = "open", User = new User { FullName = "Test User", Email = "test@test.com" } },
            new SupportTicket { Status = "pending", User = new User { FullName = "Test User", Email = "test@test.com" } },
            new SupportTicket { Status = "resolved", User = new User { FullName = "Test User", Email = "test@test.com" } }
        }.AsQueryable().BuildMock();
        var ticketRepo = new Mock<IRepository<SupportTicket>>();
        ticketRepo.Setup(r => r.Query()).Returns(tickets);

        var service = CreateService(userRepo, subRepo, invRepo, jobRepo, ticketRepo, timeProvider);

        // Act
        var result = await service.GetMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalUsers.Should().Be(2);
        result.Value.ActivePaidUsers.Should().Be(2);
        result.Value.RecurringRevenue.Should().Be(40.0m);
        result.Value.TotalRevenue.Should().Be(80.0m); // 50 + 30
        result.Value.ActiveBatchJobs.Should().Be(2);
        result.Value.PendingTickets.Should().Be(2);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenExceptionThrown_ReturnsFailure()
    {
        var userRepo = new Mock<IRepository<User>>();
        userRepo.Setup(r => r.Query()).Throws(new Exception("DB Error"));

        var service = CreateService(userRepository: userRepo);

        var result = await service.GetMetricsAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AdminDashboard.Error");
    }

    [TestMethod]
    public async Task GetMetricsAsync_WithDayTimeRange_ReturnsCorrectChartData()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2023, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var invoices = new List<Invoice>
        {
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 10, 15), TotalAmount = 50.0m },
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 10, 14), TotalAmount = 30.0m }
        }.AsQueryable().BuildMock();

        var invRepo = new Mock<IRepository<Invoice>>();
        invRepo.Setup(r => r.Query()).Returns(invoices);

        var service = CreateService(
            userRepository: CreateEmptyMock<User>(),
            subscriptionRepository: CreateEmptyMock<Subscription>(),
            invoiceRepository: invRepo,
            batchJobRepository: CreateEmptyMock<BatchJob>(),
            ticketRepository: CreateEmptyMock<SupportTicket>(),
            timeProvider: timeProvider);

        var result = await service.GetMetricsAsync("day");

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalRevenue.Should().Be(50.0m);
        result.Value.RevenueChart.Count.Should().Be(7);
        result.Value.RevenueChart.Last().Subscriptions.Should().Be(50.0m);
        result.Value.RevenueChart.ElementAt(5).Subscriptions.Should().Be(30.0m);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WithYearTimeRange_ReturnsCorrectChartData()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2023, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var invoices = new List<Invoice>
        {
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2023, 5, 15), TotalAmount = 50.0m },
            new Invoice { Status = "paid", InvoiceDate = new DateOnly(2022, 10, 14), TotalAmount = 30.0m }
        }.AsQueryable().BuildMock();

        var invRepo = new Mock<IRepository<Invoice>>();
        invRepo.Setup(r => r.Query()).Returns(invoices);

        var service = CreateService(
            userRepository: CreateEmptyMock<User>(),
            subscriptionRepository: CreateEmptyMock<Subscription>(),
            invoiceRepository: invRepo,
            batchJobRepository: CreateEmptyMock<BatchJob>(),
            ticketRepository: CreateEmptyMock<SupportTicket>(),
            timeProvider: timeProvider);

        var result = await service.GetMetricsAsync("year");

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalRevenue.Should().Be(50.0m);
        result.Value.RevenueChart.Count.Should().Be(5);
        result.Value.RevenueChart.Last().Subscriptions.Should().Be(50.0m);
        result.Value.RevenueChart.ElementAt(3).Subscriptions.Should().Be(30.0m);
    }

    [TestMethod]
    public async Task ExportMetricsAsync_WhenCalled_ReturnsCsvData()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2023, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            userRepository: CreateEmptyMock<User>(),
            subscriptionRepository: CreateEmptyMock<Subscription>(),
            invoiceRepository: CreateEmptyMock<Invoice>(),
            batchJobRepository: CreateEmptyMock<BatchJob>(),
            ticketRepository: CreateEmptyMock<SupportTicket>(),
            timeProvider: timeProvider);

        var result = await service.ExportMetricsAsync("month", new DateTime(2023, 10, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        var csvContent = System.Text.Encoding.UTF8.GetString(result.Value);
        csvContent.Should().Contain("Admin Dashboard Export");
        csvContent.Should().Contain("Time Range,month");
        csvContent.Should().Contain("Date,2023-10-15");
    }

    [TestMethod]
    public async Task ExportMetricsAsync_WhenGetMetricsFails_ReturnsFailure()
    {
        var userRepo = new Mock<IRepository<User>>();
        userRepo.Setup(r => r.Query()).Throws(new Exception("DB Error"));
        var service = CreateService(userRepository: userRepo);

        var result = await service.ExportMetricsAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AdminDashboard.Error");
    }

    private static Mock<IRepository<T>> CreateEmptyMock<T>() where T : class
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Query()).Returns(new List<T>().AsQueryable().BuildMock());
        return mock;
    }
}

using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminDashboardServiceTests
{
    private static readonly DateTimeOffset Oct15 = new(2023, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static AdminDashboardService CreateService(
        Mock<IRepository<User>>? userRepository = null,
        Mock<IRepository<BatchJob>>? batchJobRepository = null,
        Mock<IRepository<SupportTicket>>? ticketRepository = null,
        Mock<IRevenueService>? revenueService = null,
        TimeProvider? timeProvider = null)
    {
        return new AdminDashboardService(
            (userRepository ?? CreateEmptyMock<User>()).Object,
            (batchJobRepository ?? CreateEmptyMock<BatchJob>()).Object,
            (ticketRepository ?? CreateEmptyMock<SupportTicket>()).Object,
            (revenueService ?? RevenueReturning(0m, 0m, 0)).Object,
            timeProvider ?? TimeProvider.System,
            NullLogger<AdminDashboardService>.Instance
        );
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenCalled_ReturnsCalculatedMetrics()
    {
        // Arrange
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Oct15);

        var users = new List<User> { new User(), new User() }.AsQueryable().BuildMock();
        var userRepo = new Mock<IRepository<User>>();
        userRepo.Setup(r => r.Query()).Returns(users);

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

        var revenue = RevenueReturning(totalRevenue: 2_000_000m, mrr: 1_000_000m, payingCustomers: 2);
        var service = CreateService(userRepo, jobRepo, ticketRepo, revenue, timeProvider);

        // Act
        var result = await service.GetMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalUsers.Should().Be(2);
        result.Value.ActivePaidUsers.Should().Be(2);
        result.Value.RecurringRevenue.Should().Be(1_000_000m);
        result.Value.TotalRevenue.Should().Be(2_000_000m);
        result.Value.ActiveBatchJobs.Should().Be(2);
        result.Value.TotalBatchJobs.Should().Be(3);
        result.Value.PendingTickets.Should().Be(2);
    }

    [TestMethod]
    public async Task GetMetricsAsync_FillsAnEmptyJobFromItsBatchAndAiCalls()
    {
        var batch = new Batch
        {
            Name = "Shop catalog",
            Products = [new Product(), new Product(), new Product { DeletedAt = Oct15.UtcDateTime }],
        };
        var jobs = new List<BatchJob>
        {
            new()
            {
                Name = "Design run", Status = "Completed", TotalProducts = 0, ProgressPercentage = 0,
                CreatedAt = Oct15.UtcDateTime, Batch = batch, User = new User { FullName = "Lan" },
                ApiUsageRecords = [new ApiUsageRecord { CostUsd = 0.04m }, new ApiUsageRecord { CostUsd = 0.08m }],
            },
            new()
            {
                Name = "Design run", Status = "running", TotalProducts = 10, ProgressPercentage = 40,
                User = new User { FullName = "Minh" },
            },
        }.AsQueryable().BuildMock();
        var jobRepo = new Mock<IRepository<BatchJob>>();
        jobRepo.Setup(r => r.Query()).Returns(jobs);

        var result = await CreateService(batchJobRepository: jobRepo).GetMetricsAsync();

        var completed = result.Value.RecentBatchJobs.Single(job => job.UserName == "Lan");
        completed.ItemCount.Should().Be(2);
        completed.ProgressPercentage.Should().Be("100%");
        completed.BatchName.Should().Be("Shop catalog");
        completed.CreatedAt.Should().Be(Oct15.UtcDateTime);
        completed.AiCostUsd.Should().Be(0.12m);

        var running = result.Value.RecentBatchJobs.Single(job => job.UserName == "Minh");
        running.ItemCount.Should().Be(10);
        running.ProgressPercentage.Should().Be("40%");
        running.BatchName.Should().BeNull();
    }

    [TestMethod]
    [DataRow("day", "2023-10-15")]
    [DataRow("week", "2023-10-09")]
    [DataRow("halfyear", "2023-05-01")]
    [DataRow("month", "2023-10-01")]
    [DataRow("year", "2023-01-01")]
    public async Task GetMetricsAsync_ReadsTheRevenuePageFiguresFromTheStartOfTheRangeToToday(string timeRange, string start)
    {
        var revenue = RevenueReturning(0m, 0m, 0);
        var service = CreateService(revenueService: revenue, timeProvider: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Oct15));

        await service.GetMetricsAsync(timeRange);

        // MRR is read at the period's end, so the period never runs past today.
        revenue.Verify(r => r.GetMetricsAsync(
            It.Is<RevenueFilterDto>(f => f.StartDate == DateOnly.Parse(start) && f.EndDate == new DateOnly(2023, 10, 15)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenTheRevenueFiguresFail_ReturnsTheirError()
    {
        var error = new Error("Revenue.Error", "Failed.", ErrorType.Failure);
        var revenue = new Mock<IRevenueService>();
        revenue.Setup(r => r.GetMetricsAsync(It.IsAny<RevenueFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RevenueMetricsDto>(error));

        var result = await CreateService(revenueService: revenue).GetMetricsAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Revenue.Error");
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
    public async Task ExportMetricsAsync_WhenCalled_ReturnsCsvData()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Oct15);
        var service = CreateService(revenueService: RevenueReturning(2_000_000m, 1_000_000m, 2), timeProvider: timeProvider);

        var result = await service.ExportMetricsAsync("month", new DateTime(2023, 10, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        var csvContent = System.Text.Encoding.UTF8.GetString(result.Value);
        csvContent.Should().Contain("Admin Dashboard Export");
        csvContent.Should().Contain("Time Range,month");
        csvContent.Should().Contain("Date,2023-10-15");
        csvContent.Should().Contain("Total Revenue,2000000");
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

    private static Mock<IRevenueService> RevenueReturning(decimal totalRevenue, decimal mrr, int payingCustomers)
    {
        var revenue = new Mock<IRevenueService>();
        revenue.Setup(r => r.GetMetricsAsync(It.IsAny<RevenueFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RevenueMetricsDto
            {
                TotalRevenue = new MetricDetail { Value = totalRevenue },
                SubscriptionMrr = new MetricDetail { Value = mrr },
                ActiveSubscribers = new MetricDetail { Value = payingCustomers }
            }));
        return revenue;
    }

    private static Mock<IRepository<T>> CreateEmptyMock<T>() where T : class
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Query()).Returns(new List<T>().AsQueryable().BuildMock());
        return mock;
    }
}

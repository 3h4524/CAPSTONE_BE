using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminOperationsServiceTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private sealed class Data
    {
        public List<BatchJob> Jobs { get; } = [];
        public List<Product> Products { get; } = [];
        public List<DesignImage> Designs { get; } = [];
        public List<MockupImage> Mockups { get; } = [];
        public List<ListingContentEntity> Listings { get; } = [];
        public List<EtsyUploadLog> EtsyUploads { get; } = [];
        public List<PrintifyUploadLog> PrintifyUploads { get; } = [];
        public List<ApiUsageRecord> Calls { get; } = [];
    }

    private static AdminOperationsService CreateService(Data data) => new(
        Repo(data.Jobs), Repo(data.Products), Repo(data.Designs), Repo(data.Mockups), Repo(data.Listings),
        Repo(data.EtsyUploads), Repo(data.PrintifyUploads), Repo(data.Calls), new FakeTimeProvider(Today));

    private static IRepository<T> Repo<T>(List<T> rows) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        return repo.Object;
    }

    private static DateTime DaysAgo(int days) => Today.UtcDateTime.AddDays(-days);

    private static Product Product(int daysAgo) => new() { Id = Guid.NewGuid(), CreatedAt = DaysAgo(daysAgo) };

    [TestMethod]
    public async Task GetOperationsAsync_Month_CountsTheLastThirtyDaysOneBucketPerDayIncludingEmptyOnes()
    {
        var data = new Data();
        var seller = Guid.NewGuid();
        data.Jobs.AddRange([
            new BatchJob { UserId = seller, CreatedAt = DaysAgo(0) },
            new BatchJob { UserId = seller, CreatedAt = DaysAgo(0) },
            new BatchJob { UserId = Guid.NewGuid(), CreatedAt = DaysAgo(29) },
            new BatchJob { UserId = Guid.NewGuid(), CreatedAt = DaysAgo(30) }, // the previous period
            new BatchJob { UserId = seller, CreatedAt = DaysAgo(1), DeletedAt = DaysAgo(0) },
        ]);

        var result = await CreateService(data).GetOperationsAsync("month");

        result.IsSuccess.Should().BeTrue();
        var operations = result.Value;
        operations.Bucket.Should().Be("day");
        operations.PeriodStart.Should().Be(new DateOnly(2026, 10, 6));
        operations.PeriodEnd.Should().Be(new DateOnly(2026, 11, 4));
        operations.Throughput.Should().HaveCount(30);
        operations.Throughput[0].Jobs.Should().Be(1);
        operations.Throughput[^1].Jobs.Should().Be(2);
        operations.Throughput.Sum(bucket => bucket.Jobs).Should().Be(3);
        operations.ActiveSellers.Should().Be(2);
        operations.PreviousActiveSellers.Should().Be(1);
    }

    [TestMethod]
    public async Task GetOperationsAsync_Year_CountsTheLastTwelveMonthsOneBucketPerMonth()
    {
        var result = await CreateService(new Data()).GetOperationsAsync("year");

        var operations = result.Value;
        operations.Bucket.Should().Be("month");
        operations.PeriodStart.Should().Be(new DateOnly(2025, 12, 1));
        operations.Throughput.Should().HaveCount(12);
        operations.Throughput.Should().OnlyContain(bucket => bucket.Start.Day == 1);
        operations.Throughput[^1].Start.Should().Be(new DateOnly(2026, 11, 1));
    }

    [TestMethod]
    [DataRow("week", "2026-10-29", 7, "day")]
    [DataRow("halfyear", "2026-06-01", 6, "month")]
    public async Task GetOperationsAsync_CoversTheWeekAndHalfYearPeriods(string timeRange, string start, int buckets, string bucket)
    {
        var operations = (await CreateService(new Data()).GetOperationsAsync(timeRange)).Value;

        operations.PeriodStart.Should().Be(DateOnly.Parse(start));
        operations.Throughput.Should().HaveCount(buckets);
        operations.Bucket.Should().Be(bucket);
    }

    [TestMethod]
    public async Task GetOperationsAsync_FollowsTheProductsCreatedInThePeriodThroughThePipeline()
    {
        var data = new Data();
        var listedAndPublished = Product(1);
        var mockedUp = Product(2);
        var designedOnly = Product(3);
        var untouched = Product(4);
        var earlier = Product(30); // created before the period: not counted at any step
        data.Products.AddRange([listedAndPublished, mockedUp, designedOnly, untouched, earlier]);
        foreach (var product in new[] { listedAndPublished, mockedUp, designedOnly, earlier })
        {
            data.Designs.Add(new DesignImage { ProductId = product.Id });
            data.Designs.Add(new DesignImage { ProductId = product.Id });
        }
        data.Designs.Add(new DesignImage { ProductId = untouched.Id, DeletedAt = DaysAgo(0) });
        data.Mockups.AddRange([new MockupImage { ProductId = listedAndPublished.Id }, new MockupImage { ProductId = mockedUp.Id }]);
        data.Listings.Add(new ListingContentEntity { ProductId = listedAndPublished.Id });
        data.EtsyUploads.AddRange([
            new EtsyUploadLog { ProductId = listedAndPublished.Id, UploadStatus = "success" },
            new EtsyUploadLog { ProductId = mockedUp.Id, UploadStatus = "failed" },
        ]);
        data.PrintifyUploads.Add(new PrintifyUploadLog { ProductId = listedAndPublished.Id, UploadStatus = "success" });

        var funnel = (await CreateService(data).GetOperationsAsync("month")).Value.Funnel;

        funnel.Products.Should().Be(4);
        funnel.Designed.Should().Be(3);
        funnel.MockedUp.Should().Be(2);
        funnel.Listed.Should().Be(1);
        funnel.Published.Should().Be(1);
    }

    [TestMethod]
    public async Task GetOperationsAsync_TotalsAiCallsFailuresAndCostByFeatureMostUsedFirst()
    {
        var data = new Data();
        data.Calls.AddRange([
            new ApiUsageRecord { Feature = "listing", Status = "success", CostUsd = 0.01m, CreatedAt = DaysAgo(1) },
            new ApiUsageRecord { Feature = "image_generation", Status = "success", CostUsd = 0.04m, CreatedAt = DaysAgo(1) },
            new ApiUsageRecord { Feature = "image_generation", Status = "success", CostUsd = 0.04m, CreatedAt = DaysAgo(2) },
            new ApiUsageRecord { Feature = "image_generation", Status = "failed", CostUsd = 0m, CreatedAt = DaysAgo(2) },
            new ApiUsageRecord { Feature = "image_generation", Status = "success", CostUsd = 9m, CreatedAt = DaysAgo(40) },
        ]);

        var usage = (await CreateService(data).GetOperationsAsync("month")).Value.AiUsage;

        usage.Should().HaveCount(2);
        usage[0].Should().BeEquivalentTo(new { Feature = "image_generation", Calls = 3, Failed = 1, CostUsd = 0.08m });
        usage[1].Should().BeEquivalentTo(new { Feature = "listing", Calls = 1, Failed = 0, CostUsd = 0.01m });
    }
}

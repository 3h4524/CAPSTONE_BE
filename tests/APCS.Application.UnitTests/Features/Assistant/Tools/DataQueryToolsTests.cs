using System.Text.Json;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Query;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.UsageStatistics.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class DataQueryToolsTests
{
    private static AssistantToolContext Context() => new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller, null);

    [TestMethod]
    public async Task ListBatches_LimitsToThirty()
    {
        var batches = new Mock<IBatchService>();
        var items = Enumerable.Range(0, 40).Select(i => new BatchDto(Guid.NewGuid(), $"b{i}", null, null, null, "draft", DateTime.UtcNow, 1)).ToArray();
        batches.Setup(b => b.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<BatchDto>>(items));

        var outcome = await new ListBatchesTool(batches.Object).ExecuteAsync(Context(), default, default);

        JsonDocument.Parse(outcome.ModelResultJson).RootElement.GetProperty("batches").GetArrayLength().Should().Be(30);
    }

    [TestMethod]
    public async Task ListBatchProducts_LimitsProductsAndKeywords()
    {
        var batches = new Mock<IBatchService>();
        var keywords = Enumerable.Range(0, 15).Select(i => $"k{i}").ToArray();
        var items = Enumerable.Range(0, 60).Select(i => new ProductDto(Guid.NewGuid(), Guid.NewGuid(), $"p{i}", "t-shirt", null, keywords, null, null, "draft", null)).ToArray();
        var batchId = Guid.NewGuid();
        batches.Setup(b => b.ListProductsAsync(batchId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<ProductDto>>(items));

        var outcome = await new ListBatchProductsTool(batches.Object).ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"batchId":"{{batchId}}"}"""), default);

        var products = JsonDocument.Parse(outcome.ModelResultJson).RootElement.GetProperty("products");
        products.GetArrayLength().Should().Be(50);
        products[0].GetProperty("keywords").GetArrayLength().Should().Be(10);
    }

    [TestMethod]
    public async Task ListBatchProducts_WithInvalidBatchId_ReturnsInvalidArguments()
    {
        var outcome = await new ListBatchProductsTool(new Mock<IBatchService>().Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"batchId":"nope"}"""), default);

        outcome.ModelResultJson.Should().Contain("invalid_arguments");
    }

    [TestMethod]
    [DataRow(7, 7)]
    [DataRow(30, 30)]
    [DataRow(90, 90)]
    [DataRow(45, 30)]
    public async Task GetUsageOverview_NormalizesDays(int requested, int expected)
    {
        var usage = new Mock<IUsageStatisticsService>();
        usage.Setup(u => u.GetOverviewAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new UsageOverviewResponseDto(
            new UsageSummaryDto(1, 2, 3, 4m), [], new UsageAllowanceDto("Pro", 1, 10, 10m, DateOnly.FromDayNumber(1), 5, false), [], true)));

        await new GetUsageOverviewTool(usage.Object).ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"days":{{requested}}}"""), default);

        usage.Verify(u => u.GetOverviewAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetSubscriptionOverview_ReturnsPlanQuotasAndAvailablePlansButNoInvoices()
    {
        var subscriptions = new Mock<ISubscriptionService>();
        var current = new CurrentSubscriptionDto(Guid.NewGuid(), Guid.NewGuid(), "Pro", "short", "active", "monthly", 19m, DateOnly.FromDayNumber(10), DateOnly.FromDayNumber(40), null, null);
        subscriptions.Setup(s => s.GetOverviewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new SubscriptionOverviewResponseDto(
            current, [new UsageQuotaDto("images", "Images", 5, 100, "count", 5m, false)],
            [new AvailablePlanDto(Guid.NewGuid(), "Studio", "studio", null, 79.99m, 799.99m, false, [], new PlanQuotasDto(5000, 100, 50000, 100m, 500, 2000, 10))],
            [], true)));

        var outcome = await new GetSubscriptionOverviewTool(subscriptions.Object).ExecuteAsync(Context(), default, default);

        outcome.ModelResultJson.Should().Contain("Pro").And.Contain("Images").And.Contain("Studio").And.Contain("79.99");
        outcome.ModelResultJson.ToLowerInvariant().Should().NotContain("invoice");
    }

    [TestMethod]
    public async Task GetSubscriptionOverview_WhenServiceFails_ReturnsError()
    {
        var subscriptions = new Mock<ISubscriptionService>();
        subscriptions.Setup(s => s.GetOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SubscriptionOverviewResponseDto>(Error.Failure("Down", "Unavailable")));

        var outcome = await new GetSubscriptionOverviewTool(subscriptions.Object).ExecuteAsync(Context(), default, default);

        outcome.Succeeded.Should().BeFalse();
    }
}

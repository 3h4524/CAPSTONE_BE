using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using APCS.Application.Features.Commerce;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketInsightsServiceTests
{
    [TestMethod]
    public async Task CollectQueryAsync_NormalizesKeywordAndUsesEtsyDefaults()
    {
        var response = new MarketResearchJobStatus(
            Guid.NewGuid(),
            "wedding gift",
            "queued",
            0,
            0,
            null,
            DateTimeOffset.UtcNow,
            null);
        using var cancellation = new CancellationTokenSource();
        var queue = new Mock<IMarketResearchQueue>(MockBehavior.Strict);
        var ownerId = Guid.NewGuid();
        queue
            .Setup(service => service.Enqueue(
                ownerId,
                It.Is<MarketResearchRequest>(request =>
                    request.Platform == CommercePlatform.Etsy
                    && request.Queries.SequenceEqual(new[] { "wedding gift" })
                    && request.LimitPerQuery == 25
                    && request.TopN == 3)))
            .Returns(response);
        var snapshots = new Mock<IRepository<MarketListingSnapshot>>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.UserId).Returns(ownerId);
        var service = new MarketInsightsService(snapshots.Object, queue.Object, currentUser.Object);

        var result = await service.QueueCollectionAsync("  wedding   gift ", cancellation.Token);

        result.Should().BeSameAs(response);
        queue.VerifyAll();
    }

    [TestMethod]
    public async Task CollectQueryAsync_WhenKeywordIsEmpty_ThrowsWithoutCallingResearch()
    {
        var queue = new Mock<IMarketResearchQueue>(MockBehavior.Strict);
        var snapshots = new Mock<IRepository<MarketListingSnapshot>>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.UserId).Returns(Guid.NewGuid());
        var service = new MarketInsightsService(snapshots.Object, queue.Object, currentUser.Object);

        var act = () => service.QueueCollectionAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
        queue.VerifyNoOtherCalls();
    }
}

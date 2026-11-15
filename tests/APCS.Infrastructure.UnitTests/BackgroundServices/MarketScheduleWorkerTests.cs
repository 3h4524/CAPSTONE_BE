using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class MarketScheduleWorkerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T10:00:00Z");
    private readonly FakeTimeProvider time = new(Now);
    private readonly Mock<IMarketResearchScheduleService> research = new();
    private readonly Mock<IMarketResearchService> collector = new();
    private readonly Mock<IMarketInsightScheduleService> insights = new();
    private readonly Mock<IMarketResearchQueue> queue = new();
    private readonly Mock<IOpportunityService> radar = new();

    [TestMethod]
    public async Task ResearchSync_ScheduleOff_SleepsUntilSaved()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(enabled: false));

        (await ResearchWorker().TrySyncAsync(default)).Should().BeNull();
        collector.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResearchSync_NotDueYet_SleepsUntilTheRunIsDue()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now.AddMinutes(40)));

        (await ResearchWorker().TrySyncAsync(default)).Should().Be(Now.AddMinutes(40));
        collector.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResearchSync_Due_CollectsABatchAndSleepsOneInterval()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now, interval: 30));
        collector.Setup(x => x.SelectUnobservedQueriesAsync(It.IsAny<CommercePlatform>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["cat shirt", "dog mug"]);
        collector.Setup(x => x.RetrieveAndIngestAsync(It.IsAny<MarketResearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketResearchResponse(CommercePlatform.Etsy, 0, 0, []));

        var next = await ResearchWorker().TrySyncAsync(default);

        next.Should().Be(Now.AddMinutes(30));
        research.Verify(x => x.MarkRunAsync(Now, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ResearchSync_TodayCollected_SleepsUntilTheNextUtcDayWithoutAskingAgain()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now));
        collector.Setup(x => x.SelectUnobservedQueriesAsync(It.IsAny<CommercePlatform>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var worker = ResearchWorker();

        (await worker.TrySyncAsync(default)).Should().Be(DateTimeOffset.Parse("2026-11-09T00:00:00Z"));
        (await worker.TrySyncAsync(default)).Should().Be(DateTimeOffset.Parse("2026-11-09T00:00:00Z"));

        collector.Verify(x => x.SelectUnobservedQueriesAsync(It.IsAny<CommercePlatform>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        radar.Verify(x => x.RefreshAllRankingsAsync(It.IsAny<CancellationToken>()), Times.Once, "sellers' radars are ranked again once the day is collected");
    }

    [TestMethod]
    public async Task ResearchSync_RankingFails_StillSleepsUntilTheNextUtcDay()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now));
        collector.Setup(x => x.SelectUnobservedQueriesAsync(It.IsAny<CommercePlatform>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        radar.Setup(x => x.RefreshAllRankingsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        (await ResearchWorker().TrySyncAsync(default)).Should().Be(DateTimeOffset.Parse("2026-11-09T00:00:00Z"));
    }

    [TestMethod]
    public async Task ResearchSync_WindowEndsFirst_WakesAtTheWindowEnd()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now.AddHours(5), windowEnds: Now.AddHours(1)));

        (await ResearchWorker().TrySyncAsync(default)).Should().Be(Now.AddHours(1));
    }

    [TestMethod]
    public async Task ResearchSync_Fails_TriesAgainSoon()
    {
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Redis down"));

        (await ResearchWorker().TrySyncAsync(default)).Should().Be(Now + MarketScheduleSignal.RetryAfter);
    }

    [TestMethod]
    public async Task InsightSchedule_SleepsUntilTheEarliestDueSchedule()
    {
        var due = Guid.NewGuid();
        var later = Guid.NewGuid();
        insights.Setup(x => x.GetScheduledUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([due, later]);
        insights.Setup(x => x.GetForUserAsync(due, It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now, interval: 1440));
        insights.Setup(x => x.GetForUserAsync(later, It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(next: Now.AddMinutes(90)));
        queue.Setup(x => x.Enqueue(due, It.IsAny<MarketResearchRequest>()))
            .Returns(new MarketResearchJobStatus(Guid.NewGuid(), "cat shirt", "queued", 0, 0, null, Now, null));

        var next = await InsightWorker().TryEnqueueDueSchedulesAsync(default);

        next.Should().Be(Now.AddMinutes(90));
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>(), It.IsAny<MarketResearchRequest>()), Times.Once);
    }

    [TestMethod]
    public void Until_BoundsTheSleep()
    {
        MarketScheduleSignal.Until(null, Now).Should().Be(MarketScheduleSignal.LongestSleep);
        MarketScheduleSignal.Until(Now.AddDays(1), Now).Should().Be(MarketScheduleSignal.LongestSleep);
        MarketScheduleSignal.Until(Now.AddMinutes(-5), Now).Should().Be(TimeSpan.FromSeconds(1));
        MarketScheduleSignal.Until(Now.AddMinutes(20), Now).Should().Be(TimeSpan.FromMinutes(20));
    }

    [TestMethod]
    public async Task ResearchSync_ScheduleSaved_WakesTheSleepingWorker()
    {
        var signal = new MarketScheduleSignal();
        research.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Schedule(enabled: false));
        using var worker = ResearchWorker(signal);
        await worker.StartAsync(CancellationToken.None);
        await WaitForReadsAsync(1);

        signal.Research.Notify();

        await WaitForReadsAsync(2);
        await worker.StopAsync(CancellationToken.None);
    }

    private async Task WaitForReadsAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (research.Invocations.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        research.Invocations.Count.Should().Be(expected);
    }

    private MarketResearchSyncWorker ResearchWorker(MarketScheduleSignal? signal = null)
    {
        var services = new ServiceCollection().AddScoped(_ => research.Object).AddScoped(_ => collector.Object).AddScoped(_ => radar.Object).BuildServiceProvider();
        return new(services, time, NullLogger<MarketResearchSyncWorker>.Instance, signal);
    }

    private MarketInsightScheduleWorker InsightWorker()
    {
        var services = new ServiceCollection().AddScoped(_ => insights.Object).BuildServiceProvider();
        return new(services, queue.Object, time, NullLogger<MarketInsightScheduleWorker>.Instance);
    }

    private static MarketResearchSchedule Schedule(bool enabled = true, DateTimeOffset? next = null, int interval = 1440,
        DateTimeOffset? windowEnds = null) =>
        new(enabled, CommercePlatform.Etsy, ["cat shirt", "dog mug"], 25, 0, 3, interval, null, next,
            BatchSize: 5, WindowEndsAtUtc: windowEnds);
}

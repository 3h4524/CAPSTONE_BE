using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Options;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class VideoQualityWorkerTests
{
    [TestMethod]
    public void IdleDelay_StartsAtPollIntervalAndDoublesPerEmptyPoll()
    {
        var settings = new VideoQualityOptions { PollIntervalSeconds = 3, MaxIdlePollSeconds = 600 };

        VideoQualityWorker.IdleDelay(settings, 0).Should().Be(TimeSpan.FromSeconds(3));
        VideoQualityWorker.IdleDelay(settings, 1).Should().Be(TimeSpan.FromSeconds(6));
        VideoQualityWorker.IdleDelay(settings, 4).Should().Be(TimeSpan.FromSeconds(48));
    }

    [TestMethod]
    public void IdleDelay_StopsAtTheIdleMaximum()
    {
        var settings = new VideoQualityOptions { PollIntervalSeconds = 3, MaxIdlePollSeconds = 600 };

        VideoQualityWorker.IdleDelay(settings, 30).Should().Be(TimeSpan.FromSeconds(600));
    }

    [TestMethod]
    public void IdleDelay_NeverDropsBelowThePollInterval()
    {
        // A maximum below the poll interval would otherwise poll faster than configured.
        var settings = new VideoQualityOptions { PollIntervalSeconds = 10, MaxIdlePollSeconds = 1 };

        VideoQualityWorker.IdleDelay(settings, 5).Should().Be(TimeSpan.FromSeconds(10));
    }
}

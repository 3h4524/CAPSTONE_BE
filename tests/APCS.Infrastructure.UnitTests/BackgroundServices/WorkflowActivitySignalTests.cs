using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class WorkflowActivitySignalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task WaitAsync_NotifiedAfterTheVersionWasRead_ReturnsAtOnce()
    {
        var signal = new ActivitySignal();
        var seen = signal.Version;
        signal.Notify();

        var notified = await signal.WaitAsync(seen, Timeout.InfiniteTimeSpan, CancellationToken.None).WaitAsync(Patience);

        notified.Should().BeTrue();
    }

    [TestMethod]
    public async Task WaitAsync_NotifiedWhileWaiting_WakesEveryWaiter()
    {
        var signal = new ActivitySignal();
        var seen = signal.Version;
        var first = signal.WaitAsync(seen, Timeout.InfiniteTimeSpan, CancellationToken.None);
        var second = signal.WaitAsync(seen, Timeout.InfiniteTimeSpan, CancellationToken.None);
        first.IsCompleted.Should().BeFalse();

        signal.Notify();

        (await first.WaitAsync(Patience)).Should().BeTrue();
        (await second.WaitAsync(Patience)).Should().BeTrue();
    }

    [TestMethod]
    public async Task WaitAsync_NoNotification_ReturnsFalseAtTheTimeout()
    {
        var time = new FakeTimeProvider();
        var signal = new ActivitySignal();
        var wait = signal.WaitAsync(signal.Version, TimeSpan.FromHours(6), CancellationToken.None, time);

        time.Advance(TimeSpan.FromHours(5));
        wait.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromHours(1));

        (await wait.WaitAsync(Patience)).Should().BeFalse();
    }

    [TestMethod]
    public void Notify_OfOneKind_DoesNotMoveTheOther()
    {
        var signals = new WorkflowActivitySignal();

        signals.Publish.Notify();

        signals.Publish.Version.Should().Be(1);
        signals.Media.Version.Should().Be(0);
    }
}

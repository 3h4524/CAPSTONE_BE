using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class RunGroupStartQueueTests
{
    private static RunGroupStartRequest Request(Guid? group = null) => new(group ?? Guid.NewGuid(), Guid.NewGuid(), 5m, AutoApprove: true);

    [TestMethod]
    public async Task Enqueue_HandsTheGroupToTheWorkerAndIsPendingUntilItIsDone()
    {
        var queue = new RunGroupStartQueue();
        var request = Request();

        queue.Enqueue(request);

        queue.IsPending(request.GroupId).Should().BeTrue("the seller's screen says the run is getting ready");
        (await queue.DequeueAsync(CancellationToken.None)).Should().Be(request);
        queue.IsPending(request.GroupId).Should().BeTrue("the worker has not finished with it");
        queue.Done(request.GroupId);
        queue.IsPending(request.GroupId).Should().BeFalse();
    }

    [TestMethod]
    public async Task Enqueue_TheSameGroupTwiceWhilePending_IsMadeReadyOnce()
    {
        var queue = new RunGroupStartQueue();
        var group = Guid.NewGuid();
        var first = Request(group);

        queue.Enqueue(first);
        queue.Enqueue(Request(group));

        (await queue.DequeueAsync(CancellationToken.None)).Should().Be(first);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var second = async () => await queue.DequeueAsync(cancellation.Token);
        await second.Should().ThrowAsync<OperationCanceledException>("the second request was dropped");
    }

    [TestMethod]
    public void IsPending_AGroupNeverQueued_IsFalse() => new RunGroupStartQueue().IsPending(Guid.NewGuid()).Should().BeFalse();
}

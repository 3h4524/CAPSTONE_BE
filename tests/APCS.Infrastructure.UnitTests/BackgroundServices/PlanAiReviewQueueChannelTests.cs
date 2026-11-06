using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class PlanAiReviewQueueChannelTests
{
    [TestMethod]
    public async Task TryRequest_HoldsOneRequestAndIsBusyUntilTheJobTakesIt()
    {
        var queue = new PlanAiReviewQueueChannel();
        var admin = Guid.NewGuid();

        queue.TryRequest(admin).Should().BeTrue();

        queue.IsBusy.Should().BeTrue("the page sees the request before the job picks it up");
        queue.TryRequest(Guid.NewGuid()).Should().BeFalse("one review is already queued");
        (await queue.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).Should().Be(admin);
    }

    [TestMethod]
    public async Task WaitAsync_WithNoRequest_ReturnsNullAfterTheTimeout()
    {
        var queue = new PlanAiReviewQueueChannel();

        (await queue.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)).Should().BeNull();
    }

    [TestMethod]
    public void Generating_IsBusyOnlyWhileTheScopeIsOpen()
    {
        var queue = new PlanAiReviewQueueChannel();

        using (queue.Generating())
        {
            queue.IsBusy.Should().BeTrue();
            queue.TryRequest(Guid.NewGuid()).Should().BeFalse();
        }

        queue.IsBusy.Should().BeFalse();
        queue.TryRequest(Guid.NewGuid()).Should().BeTrue();
    }
}

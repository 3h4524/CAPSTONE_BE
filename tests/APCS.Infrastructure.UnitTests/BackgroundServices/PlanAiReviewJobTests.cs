using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class PlanAiReviewJobTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Hands the job one scripted wake-up per pass, then waits until the job is stopped.</summary>
    private sealed class ScriptedQueue(params Guid?[] wakeUps) : IPlanAiReviewQueue
    {
        private readonly Queue<Guid?> _wakeUps = new(wakeUps);

        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenMarks { get; private set; }

        public bool IsBusy => OpenMarks > 0;

        public bool TryRequest(Guid adminId) => false;

        public async Task<Guid?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (_wakeUps.TryDequeue(out var next)) return next;

            Drained.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public IDisposable Generating()
        {
            OpenMarks++;
            return new Mark(this);
        }

        private sealed class Mark(ScriptedQueue queue) : IDisposable
        {
            public void Dispose() => queue.OpenMarks--;
        }
    }

    private static async Task RunUntilDrainedAsync(ScriptedQueue queue, Mock<IPlanAiReviewService> service)
    {
        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var job = new PlanAiReviewJob(services, queue, NullLogger<PlanAiReviewJob>.Instance);

        await job.StartAsync(CancellationToken.None);
        await queue.Drained.Task.WaitAsync(Patience);
        await job.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task AnAdminsRequest_WritesAReviewWithoutAskingIfOneIsDue()
    {
        var admin = Guid.NewGuid();
        var service = new Mock<IPlanAiReviewService>();
        var queue = new ScriptedQueue(admin);

        await RunUntilDrainedAsync(queue, service);

        service.Verify(x => x.GenerateAsync(admin, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.IsDueAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AScheduledCheck_WhenAReviewIsDue_WritesOneForNobody()
    {
        var service = new Mock<IPlanAiReviewService>();
        service.Setup(x => x.IsDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await RunUntilDrainedAsync(new ScriptedQueue((Guid?)null), service);

        service.Verify(x => x.GenerateAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AScheduledCheck_WhenNoReviewIsDue_CallsNoModel()
    {
        var service = new Mock<IPlanAiReviewService>();
        service.Setup(x => x.IsDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await RunUntilDrainedAsync(new ScriptedQueue((Guid?)null), service);

        service.Verify(x => x.GenerateAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task WhileAReviewIsBeingWritten_TheQueueIsBusy()
    {
        var busy = false;
        var service = new Mock<IPlanAiReviewService>();
        var queue = new ScriptedQueue(Guid.NewGuid());
        service.Setup(x => x.GenerateAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                busy = queue.IsBusy;
                return Task.CompletedTask;
            });

        await RunUntilDrainedAsync(queue, service);

        busy.Should().BeTrue("the page shows a review is being written");
        queue.IsBusy.Should().BeFalse("the mark is cleared when the run ends");
    }

    [TestMethod]
    public async Task AFailedReview_ClearsTheBusyMarkAndKeepsTheJobRunning()
    {
        var service = new Mock<IPlanAiReviewService>();
        service.Setup(x => x.GenerateAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The model is down."));
        service.Setup(x => x.IsDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var queue = new ScriptedQueue(Guid.NewGuid(), null);

        await RunUntilDrainedAsync(queue, service);

        queue.IsBusy.Should().BeFalse();
        service.Verify(x => x.IsDueAsync(It.IsAny<CancellationToken>()), Times.Once, "the next pass still ran");
    }

    [TestMethod]
    public async Task AFailedCheck_KeepsTheJobRunning()
    {
        var service = new Mock<IPlanAiReviewService>();
        service.SetupSequence(x => x.IsDueAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The database is down."))
            .ReturnsAsync(true);

        await RunUntilDrainedAsync(new ScriptedQueue(null, null), service);

        service.Verify(x => x.GenerateAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Stopping_WhileWaiting_EndsTheJobCleanly()
    {
        var queue = new ScriptedQueue();
        var service = new Mock<IPlanAiReviewService>();
        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var job = new PlanAiReviewJob(services, queue, NullLogger<PlanAiReviewJob>.Instance);

        await job.StartAsync(CancellationToken.None);
        await queue.Drained.Task.WaitAsync(Patience);
        await job.StopAsync(CancellationToken.None);

        job.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }
}

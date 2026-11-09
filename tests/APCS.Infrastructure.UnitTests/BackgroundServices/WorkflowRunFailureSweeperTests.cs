using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class WorkflowRunFailureSweeperTests
{
    private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-08T00:00:00Z"));
    private readonly WorkflowActivitySignal activity = new();
    private readonly Mock<IWorkflowRunReconciler> reconciler = new();
    private int sweeps;

    [TestInitialize]
    public void Setup() =>
        reconciler.Setup(x => x.FailRunsWithFailedJobsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref sweeps)).ReturnsAsync(0);

    [TestMethod]
    public async Task Sweeper_NoMediaWorkInFlight_StopsQueryingUntilSignalled()
    {
        InFlight(false);
        using var sweeper = await StartAsync();

        await AdvanceAsync(TimeSpan.FromHours(1));
        sweeps.Should().Be(1, "an idle sweeper waits for the signal instead of polling");

        activity.Media.Notify();
        await AdvanceAsync(WorkflowRunFailureSweeper.Interval);
        await WaitForSweepsAsync(2);
    }

    [TestMethod]
    public async Task Sweeper_MediaWorkInFlight_SweepsEveryInterval()
    {
        InFlight(true);
        using var sweeper = await StartAsync();

        await AdvanceAsync(WorkflowRunFailureSweeper.Interval);
        await WaitForSweepsAsync(2);
        await AdvanceAsync(WorkflowRunFailureSweeper.Interval);
        await WaitForSweepsAsync(3);
    }

    [TestMethod]
    public async Task Sweeper_Idle_StillLooksOnceAtTheSafetyCheck()
    {
        InFlight(false);
        using var sweeper = await StartAsync();

        await AdvanceAsync(WorkflowActivitySignal.IdleSafetyCheck);
        await AdvanceAsync(WorkflowRunFailureSweeper.Interval);

        await WaitForSweepsAsync(2);
    }

    private void InFlight(bool value) =>
        reconciler.Setup(x => x.HasMediaWorkInFlightAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(value);

    private async Task<Running> StartAsync()
    {
        var services = new ServiceCollection().AddScoped(_ => reconciler.Object).BuildServiceProvider();
        var sweeper = new WorkflowRunFailureSweeper(services.GetRequiredService<IServiceScopeFactory>(), activity, time,
            NullLogger<WorkflowRunFailureSweeper>.Instance);
        await sweeper.StartAsync(CancellationToken.None);
        await WaitForSweepsAsync(1);
        return new Running(sweeper, services);
    }

    /// <summary>Lets the sweeper reach its next wait, then moves the fake clock.</summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        await Task.Delay(100);
        time.Advance(by);
        await Task.Delay(100);
    }

    private async Task WaitForSweepsAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref sweeps) < expected && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Volatile.Read(ref sweeps).Should().Be(expected);
    }

    private sealed class Running(WorkflowRunFailureSweeper sweeper, ServiceProvider services) : IDisposable
    {
        public void Dispose()
        {
            sweeper.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            sweeper.Dispose();
            services.Dispose();
        }
    }
}

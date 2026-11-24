using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class IdeogramReadySignalsTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);
    private readonly IdeogramReadySignals ready = new();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Ready_ForAGenerationNobodyWaitsFor_DoesNothing()
    {
        ready.Ready("g1").Should().BeFalse();
    }

    [TestMethod]
    public async Task WaitAsync_EndsAsSoonAsIdeogramCalls()
    {
        ready.Watch("g1");
        var waiting = ready.WaitAsync("g1", Long, TestContext.CancellationToken);
        waiting.IsCompleted.Should().BeFalse();

        ready.Ready("g1").Should().BeTrue();

        await waiting.WaitAsync(Soon, TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task WaitAsync_ACallDuringTheLastCheck_EndsTheNextWaitAtOnce()
    {
        ready.Watch("g1");
        ready.Ready("g1");

        await ready.WaitAsync("g1", Long, TestContext.CancellationToken).WaitAsync(Soon, TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task WaitAsync_ACallIsUsedOnce_TheWaitAfterItWaitsForTheNext()
    {
        ready.Watch("g1");
        ready.Ready("g1");
        await ready.WaitAsync("g1", Long, TestContext.CancellationToken);

        var next = ready.WaitAsync("g1", Long, TestContext.CancellationToken);

        next.IsCompleted.Should().BeFalse();
        ready.Ready("g1").Should().BeTrue();
        await next.WaitAsync(Soon, TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task WaitAsync_WithoutACall_WaitsTheWholeTime()
    {
        ready.Watch("g1");

        var started = DateTime.UtcNow;
        await ready.WaitAsync("g1", TimeSpan.FromMilliseconds(200), TestContext.CancellationToken);

        (DateTime.UtcNow - started).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [TestMethod]
    public void Forget_ACallAfterwardsDoesNothing()
    {
        ready.Watch("g1");
        ready.Forget("g1");

        ready.Ready("g1").Should().BeFalse();
    }
}

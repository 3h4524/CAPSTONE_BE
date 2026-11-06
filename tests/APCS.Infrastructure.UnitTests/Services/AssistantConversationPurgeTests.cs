using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class AssistantConversationPurgeTests
{
    [TestMethod]
    public async Task Queue_CoalescesRepeatedRequestsIntoOne()
    {
        var queue = new AssistantConversationPurgeQueueChannel();
        queue.RequestPurge();
        queue.RequestPurge();
        queue.RequestPurge();

        await queue.WaitForRequestAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var second = async () => await queue.WaitForRequestAsync(cancellation.Token);

        await second.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task Worker_PurgesOnStartupAndAgainWhenRequested()
    {
        var purged = new SemaphoreSlim(0);
        var repository = new Mock<IAssistantConversationRepository>();
        repository.Setup(r => r.PurgeDeletedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2).Callback(() => purged.Release());
        var services = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        var queue = new AssistantConversationPurgeQueueChannel();
        var worker = new AssistantConversationPurgeWorker(queue, services, NullLogger<AssistantConversationPurgeWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        (await purged.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("the startup sweep must run");
        queue.RequestPurge();
        (await purged.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("a request must trigger another purge");
        await worker.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Worker_KeepsRunningAfterAPurgeFails()
    {
        var calls = new SemaphoreSlim(0);
        var attempt = 0;
        var repository = new Mock<IAssistantConversationRepository>();
        repository.Setup(r => r.PurgeDeletedAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            calls.Release();
            return ++attempt == 1 ? throw new InvalidOperationException("db down") : Task.FromResult(0);
        });
        var services = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
        var queue = new AssistantConversationPurgeQueueChannel();
        var worker = new AssistantConversationPurgeWorker(queue, services, NullLogger<AssistantConversationPurgeWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        (await calls.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        queue.RequestPurge();
        (await calls.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("the worker must survive the first failure");
        await worker.StopAsync(CancellationToken.None);
    }
}

using System.Collections.Concurrent;
using System.Security.Claims;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class AssistantTurnWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid RequestUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly AssistantTurnQueueChannel queue = new();
    private readonly Mock<IAssistantConversationRepository> repository = new();
    private readonly ConcurrentDictionary<Guid, Run> runs = new();
    private readonly SemaphoreSlim ran = new(0);
    private Func<AssistantChatSession, CancellationToken, Task> behaviour = (_, _) => Task.CompletedTask;

    private sealed record Run(Guid? CurrentUserId, ActingUserContext Scope);

    private static AssistantChatSession Session(Guid userId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), userId, new AssistantCaller(true, false), "hi", null, []);

    private AssistantTurnWorker Worker()
    {
        var services = new ServiceCollection();
        services.AddScoped<ActingUserContext>();
        services.AddSingleton<IHttpContextAccessor>(new ForeignRequestAccessor());
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddSingleton(repository.Object);
        services.AddScoped<IAssistantTurnRunner>(provider => new RecordingRunner(
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<ActingUserContext>(), this));
        return new AssistantTurnWorker(
            queue,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new AssistantTurnOptions()),
            new FakeTimeProvider(Now),
            NullLogger<AssistantTurnWorker>.Instance);
    }

    [TestMethod]
    public async Task Worker_RunsEachTurnInItsOwnScopeActingForTheUserWhoSentIt()
    {
        var first = Session(Guid.NewGuid());
        var second = Session(Guid.NewGuid());
        var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(first);
        queue.Enqueue(second);
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await worker.StopAsync(CancellationToken.None);

        runs[first.AssistantMessageId].CurrentUserId.Should().Be(first.UserId);
        runs[second.AssistantMessageId].CurrentUserId.Should().Be(second.UserId);
        runs[first.AssistantMessageId].Scope.Should().NotBeSameAs(runs[second.AssistantMessageId].Scope);
    }

    [TestMethod]
    public async Task Worker_OnStartup_MarksTurnsOlderThanTheTimeoutAsInterrupted()
    {
        var swept = new TaskCompletionSource<DateTime>();
        repository.Setup(r => r.MarkStreamingMessagesInterruptedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((cutoff, _) => swept.TrySetResult(cutoff))
            .ReturnsAsync(1);
        var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        var cutoff = await swept.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        cutoff.Should().Be(Now.UtcDateTime - new AssistantTurnOptions().StaleAfter);
        cutoff.Should().BeBefore(Now.UtcDateTime - TimeSpan.FromSeconds(new AssistantTurnOptions().TurnTimeoutSeconds));
    }

    [TestMethod]
    public async Task Worker_WhenTheSweepFails_StillRunsTurns()
    {
        repository.Setup(r => r.MarkStreamingMessagesInterruptedAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var session = Session(Guid.NewGuid());
        var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(session);
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await worker.StopAsync(CancellationToken.None);

        runs.Should().ContainKey(session.AssistantMessageId);
    }

    [TestMethod]
    public async Task Worker_WhenATurnFails_KeepsRunningTheNextOne()
    {
        var failing = Session(Guid.NewGuid());
        var next = Session(Guid.NewGuid());
        behaviour = (session, _) => session == failing ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(failing);
        queue.Enqueue(next);
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await worker.StopAsync(CancellationToken.None);

        runs.Should().ContainKey(next.AssistantMessageId);
    }

    [TestMethod]
    public async Task StopAsync_CancelsRunningTurnsAndWaitsForThemToFinish()
    {
        var finished = false;
        behaviour = async (_, stoppingToken) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(50, CancellationToken.None);
                finished = true;
            }
        };
        var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        queue.Enqueue(Session(Guid.NewGuid()));
        (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await worker.StopAsync(CancellationToken.None);

        finished.Should().BeTrue();
    }

    private sealed class RecordingRunner(ICurrentUser currentUser, ActingUserContext scope, AssistantTurnWorkerTests test) : IAssistantTurnRunner
    {
        public async Task RunAsync(AssistantChatSession session, CancellationToken stoppingToken)
        {
            test.runs[session.AssistantMessageId] = new Run(currentUser.UserId, scope);
            test.ran.Release();
            await test.behaviour(session, stoppingToken);
        }
    }

    private sealed class ForeignRequestAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, RequestUserId.ToString())], "test"))
            };
            set { }
        }
    }
}

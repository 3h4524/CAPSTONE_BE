using System.Collections.Concurrent;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class AssistantTurnWorker(
    IAssistantTurnQueue queue,
    IServiceScopeFactory scopes,
    IOptions<AssistantTurnOptions> options,
    TimeProvider timeProvider,
    ILogger<AssistantTurnWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Task> running = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepInterruptedTurnsAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            AssistantChatSession session;
            try
            {
                session = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var turn = Task.Run(() => RunTurnAsync(session, stoppingToken), CancellationToken.None);
            running.TryAdd(session.AssistantMessageId, turn);
            _ = turn.ContinueWith(_ => running.TryRemove(session.AssistantMessageId, out _), TaskScheduler.Default);
        }

        await Task.WhenAll(running.Values);
    }

    private async Task RunTurnAsync(AssistantChatSession session, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs(session.UserId);
            await scope.ServiceProvider.GetRequiredService<IAssistantTurnRunner>().RunAsync(session, stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Assistant turn {TurnId} could not run.", session.AssistantMessageId);
        }
    }

    private async Task SweepInterruptedTurnsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var createdBefore = timeProvider.GetUtcNow().UtcDateTime - options.Value.StaleAfter;
            var interrupted = await scope.ServiceProvider.GetRequiredService<IAssistantConversationRepository>()
                .MarkStreamingMessagesInterruptedAsync(createdBefore, stoppingToken);
            if (interrupted > 0)
                logger.LogInformation("Marked {Count} unfinished assistant answers as interrupted.", interrupted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Marking unfinished assistant answers as interrupted failed.");
        }
    }
}

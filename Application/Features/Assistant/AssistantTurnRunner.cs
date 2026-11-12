using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Assistant;

public sealed class AssistantTurnRunner(
    AssistantAgentLoop agentLoop,
    IAssistantTurnBuffer buffer,
    IOptions<AssistantTurnOptions> options,
    TimeProvider timeProvider,
    ILogger<AssistantTurnRunner> logger) : IAssistantTurnRunner
{
    public static readonly TimeSpan StopPollInterval = TimeSpan.FromSeconds(1);

    public async Task RunAsync(AssistantChatSession session, CancellationToken stoppingToken)
    {
        var turnId = session.AssistantMessageId;
        using var control = new AssistantTurnControl(options.Value.TurnTimeout, timeProvider, stoppingToken);
        using var watching = new CancellationTokenSource();
        var stopWatcher = WatchStopAsync(turnId, control, watching.Token);
        try
        {
            await foreach (var streamEvent in agentLoop.RunAsync(session, control.Token, () => control.CancelledAs))
                await buffer.AppendAsync(turnId, streamEvent.Type, streamEvent.DataJson(), CancellationToken.None);
        }
        catch (OperationCanceledException) when (control.Token.IsCancellationRequested)
        {
            await AppendEndAsync(turnId, control.CancelledAs);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Assistant turn {TurnId} failed.", turnId);
            await AppendEndAsync(turnId, AssistantTurnEnd.Failed());
        }
        finally
        {
            await watching.CancelAsync();
            await stopWatcher;
        }
    }

    private async Task AppendEndAsync(Guid turnId, AssistantTurnEnd end)
    {
        var error = end.Error ?? AssistantTurnEnd.Failed().Error!;
        try
        {
            await buffer.AppendAsync(turnId, AssistantStreamEvent.ErrorType, AssistantStreamEvent.Error(error.Code, error.Message).DataJson(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not write the end of assistant turn {TurnId}.", turnId);
        }
    }

    private async Task WatchStopAsync(Guid turnId, AssistantTurnControl control, CancellationToken cancellationToken)
    {
        try
        {
            await using var stopSignal = await buffer.WatchStopAsync(turnId, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await buffer.IsStopRequestedAsync(turnId, cancellationToken))
                {
                    control.Stop();
                    return;
                }

                await stopSignal.WaitAsync(StopPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Watching for a stop of assistant turn {TurnId} failed.", turnId);
        }
    }
}

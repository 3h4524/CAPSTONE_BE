using APCS.Application.Features.Workflows;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

public sealed class SceneVideoNotifier(IHubContext<WorkflowHub> hub, ILogger<SceneVideoNotifier> logger) : ISceneVideoNotifier
{
    public async Task ChangedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients.Group(WorkflowHub.GroupName(runId))
                .SendAsync("SceneVideosChanged", runId.ToString("D"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The job transaction has already committed. A disconnected client refreshes on reconnect.
            logger.LogWarning(ex, "Could not notify clients of scene changes for workflow {RunId}", runId);
        }
    }
}

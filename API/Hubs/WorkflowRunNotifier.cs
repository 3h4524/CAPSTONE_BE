using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

/// <summary>
/// Sends "RunChanged" to the pages following a run and to the pages following its run group, and "RunGroupChanged"
/// to the latter when the group itself changes. The event names what changed; pages read it again.
/// </summary>
public sealed class WorkflowRunNotifier(IHubContext<WorkflowHub> hub, ILogger<WorkflowRunNotifier> logger) : IWorkflowRunNotifier
{
    public async Task RunChangedAsync(WorkflowRun run, CancellationToken cancellationToken = default)
    {
        var change = new { runId = run.Id.ToString("D"), groupId = run.RunGroupId?.ToString("D"), status = run.Status, revision = run.Revision };
        try
        {
            await hub.Clients.Group(WorkflowHub.GroupName(run.Id)).SendAsync("RunChanged", change, CancellationToken.None);
            if (run.RunGroupId is Guid groupId)
                await hub.Clients.Group(WorkflowHub.RunGroupName(groupId)).SendAsync("RunChanged", change, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The change has committed; a page that missed it reads the run again when it reconnects.
            logger.LogWarning(exception, "Could not notify clients of a change to workflow run {RunId}", run.Id);
        }
    }

    public async Task ProgressAsync(Guid runId, Guid? groupId, string stage, int progress, CancellationToken cancellationToken = default)
    {
        var change = new { runId = runId.ToString("D"), groupId = groupId?.ToString("D"), stage, progress };
        try
        {
            await hub.Clients.Group(WorkflowHub.GroupName(runId)).SendAsync("RunProgress", change, CancellationToken.None);
            if (groupId is Guid id)
                await hub.Clients.Group(WorkflowHub.RunGroupName(id)).SendAsync("RunProgress", change, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify clients of the progress of workflow run {RunId}", runId);
        }
    }

    public async Task GroupChangedAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients.Group(WorkflowHub.RunGroupName(groupId))
                .SendAsync("RunGroupChanged", new { groupId = groupId.ToString("D") }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify clients of a change to run group {GroupId}", groupId);
        }
    }
}

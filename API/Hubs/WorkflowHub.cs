using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

[Authorize]
public sealed class WorkflowHub(IRepository<WorkflowRun> runs, IRepository<WorkflowRunGroup> groups, ICurrentUser user) : Hub
{
    internal static string GroupName(Guid runId) => $"workflow:{runId:D}";

    internal static string RunGroupName(Guid groupId) => $"workflow-group:{groupId:D}";

    public async Task JoinWorkflow(Guid runId)
    {
        var run = await runs.GetByIdAsync(runId, Context.ConnectionAborted);
        if (user.UserId is null || run?.UserId != user.UserId)
            throw new HubException("Workflow not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId), Context.ConnectionAborted);
    }

    public Task LeaveWorkflow(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId), Context.ConnectionAborted);

    /// <summary>Follows every run of a batch video run: one event per change of any of them.</summary>
    public async Task JoinRunGroup(Guid groupId)
    {
        var group = await groups.GetByIdAsync(groupId, Context.ConnectionAborted);
        if (user.UserId is null || group?.UserId != user.UserId)
            throw new HubException("Run not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, RunGroupName(groupId), Context.ConnectionAborted);
    }

    public Task LeaveRunGroup(Guid groupId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroupName(groupId), Context.ConnectionAborted);
}

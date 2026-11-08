using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

[Authorize]
public sealed class WorkflowHub(IRepository<WorkflowRun> runs, ICurrentUser user) : Hub
{
    internal static string GroupName(Guid runId) => $"workflow:{runId:D}";

    public async Task JoinWorkflow(Guid runId)
    {
        var run = await runs.GetByIdAsync(runId, Context.ConnectionAborted);
        if (user.UserId is null || run?.UserId != user.UserId)
            throw new HubException("Workflow not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId), Context.ConnectionAborted);
    }

    public Task LeaveWorkflow(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId), Context.ConnectionAborted);
}

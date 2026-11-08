using APCS.Api.Hubs;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace APCS.Api.UnitTests.Hubs;

[TestClass]
public sealed class WorkflowHubTests
{
    [TestMethod]
    public async Task JoinWorkflow_Owner_JoinsOnlyTheRequestedWorkflow()
    {
        var owner = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var runs = new Mock<IRepository<WorkflowRun>>();
        runs.Setup(x => x.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowRun { Id = runId, UserId = owner });
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(owner);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(x => x.ConnectionId).Returns("connection");
        var groups = new Mock<IGroupManager>();
        var hub = new WorkflowHub(runs.Object, user.Object) { Context = context.Object, Groups = groups.Object };

        await hub.JoinWorkflow(runId);

        groups.Verify(x => x.AddToGroupAsync("connection", $"workflow:{runId:D}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task JoinWorkflow_OtherUser_CannotSubscribe()
    {
        var runId = Guid.NewGuid();
        var runs = new Mock<IRepository<WorkflowRun>>();
        runs.Setup(x => x.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowRun { Id = runId, UserId = Guid.NewGuid() });
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var groups = new Mock<IGroupManager>();
        var hub = new WorkflowHub(runs.Object, user.Object) { Context = Mock.Of<HubCallerContext>(), Groups = groups.Object };

        var action = () => hub.JoinWorkflow(runId);

        await action.Should().ThrowAsync<HubException>();
        groups.Verify(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

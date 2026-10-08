using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowServiceTests
{
    [TestMethod]
    public async Task SaveAsync_InvalidRequest_InvokesValidatorBeforePersistence()
    {
        var fixture = new WorkflowTestData();
        var definition = WorkflowTestData.Definition(fixture.AddProduct());
        var result = await fixture.WorkflowService.SaveAsync(null, new("", "", definition), default);
        result.IsFailure.Should().BeTrue();
        fixture.WorkflowRows.Should().BeEmpty();
        fixture.Unit.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveAsync_ForeignWorkflow_DoesNotOverwriteDefinition()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct()); workflow.UserId = Guid.NewGuid();
        var definition = workflow.Definition;
        var result = await fixture.WorkflowService.SaveAsync(workflow.Id, new("Stolen", "", WorkflowJson.Read<WorkflowDefinition>(definition), 4), default);
        result.Error.Code.Should().Be("WorkflowNotFound");
        workflow.Name.Should().Be("Video workflow");
        workflow.Definition.Should().Be(definition);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_ForeignWorkflow_DoesNotSoftDelete()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct()); workflow.UserId = Guid.NewGuid();
        var result = await fixture.WorkflowService.DeleteAsync(workflow.Id, 4, default);
        result.Error.Code.Should().Be("WorkflowNotFound");
        workflow.DeletedAt.Should().BeNull();
        workflow.Revision.Should().Be(4);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task GetAsync_ForeignOrDeletedWorkflow_ReturnsNotFound(bool foreign, bool deleted)
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        if (foreign) workflow.UserId = Guid.NewGuid();
        if (deleted) workflow.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var result = await fixture.WorkflowService.GetAsync(workflow.Id, default);
        result.Error.Code.Should().Be("WorkflowNotFound");
    }

    [TestMethod]
    public async Task ListAsync_SeveralOwnersAndDeletedRows_OnlyReturnsCurrentOwnerInRecencyOrder()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var first = fixture.AddWorkflow(product);
        var latest = fixture.AddWorkflow(product); latest.UpdatedAt = WorkflowTestData.Now.AddMinutes(1).UtcDateTime;
        var deleted = fixture.AddWorkflow(product); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var foreign = fixture.AddWorkflow(product); foreign.UserId = Guid.NewGuid();
        var result = await fixture.WorkflowService.ListAsync(default);
        result.Value.Select(x => x.Id).Should().Equal(latest.Id, first.Id);
    }

    [TestMethod]
    public async Task SaveAsync_NewWorkflow_PersistsNormalizedDefinitionWithClockAndCancellation()
    {
        var fixture = new WorkflowTestData();
        var definition = WorkflowTestData.Definition(fixture.AddProduct());
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.WorkflowService.SaveAsync(null, new("  Product video  ", "  Approved mockups  ", definition), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        var stored = fixture.WorkflowRows.Single();
        stored.UserId.Should().Be(WorkflowTestData.Owner);
        stored.Name.Should().Be("Product video");
        stored.Description.Should().Be("Approved mockups");
        stored.SchemaVersion.Should().Be(2);
        stored.Revision.Should().Be(1);
        stored.CreatedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        WorkflowJson.Read<WorkflowDefinition>(stored.Definition).Nodes.Select(x => x.Type).Should().Equal(WorkflowCapabilityRegistry.Pipeline);
        fixture.State.Verify(x => x.LockOwnerAsync(WorkflowTestData.Owner, cancellation.Token), Times.Once);
        fixture.Unit.Verify(x => x.SaveChangesAsync(cancellation.Token), Times.Once);
        fixture.Transaction.Verify(x => x.CommitAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task SaveAsync_CurrentRevision_UpdatesAndIncrementsRevision()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        var result = await fixture.WorkflowService.SaveAsync(workflow.Id, new("Updated", "", WorkflowJson.Read<WorkflowDefinition>(workflow.Definition), 4), default);
        result.Value.Revision.Should().Be(5);
        workflow.Name.Should().Be("Updated");
        workflow.UpdatedAt.Should().Be(WorkflowTestData.Now.AddMinutes(2).UtcDateTime);
        fixture.Workflows.Verify(x => x.AddAsync(It.IsAny<Workflow>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveAsync_StaleRevision_DoesNotPersistOrCommit()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var result = await fixture.WorkflowService.SaveAsync(workflow.Id, new("Changed", "", WorkflowJson.Read<WorkflowDefinition>(workflow.Definition), 3), default);
        result.Error.Code.Should().Be("StaleRevision");
        workflow.Revision.Should().Be(4);
        workflow.Name.Should().Be("Video workflow");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveAsync_DuplicateNameIgnoringCase_RejectsWithoutCommit()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var result = await fixture.WorkflowService.SaveAsync(null, new("VIDEO WORKFLOW", "", WorkflowJson.Read<WorkflowDefinition>(workflow.Definition)), default);
        result.Error.Code.Should().Be("DuplicateWorkflowName");
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_CurrentRevision_SoftDeletesAndPreservesDefinition()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var definition = workflow.Definition;
        var result = await fixture.WorkflowService.DeleteAsync(workflow.Id, 4, default);
        result.IsSuccess.Should().BeTrue();
        workflow.DeletedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        workflow.Revision.Should().Be(5);
        workflow.Definition.Should().Be(definition);
        fixture.Workflows.Verify(x => x.RemoveAsync(It.IsAny<Workflow>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_StaleRevision_DoesNotDelete()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var result = await fixture.WorkflowService.DeleteAsync(workflow.Id, 3, default);
        result.Error.Code.Should().Be("StaleRevision");
        workflow.DeletedAt.Should().BeNull();
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

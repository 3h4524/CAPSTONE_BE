using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Confirmations;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class CreationToolsTests
{
    private static readonly Guid BatchId = Guid.NewGuid();

    private readonly Mock<IWorkflowService> workflows = new();
    private readonly Mock<IBatchService> batches = new();
    private readonly Mock<IAssistantPendingActionRepository> pendingActions = new();
    private readonly List<AssistantPendingAction> added = [];

    public CreationToolsTests()
    {
        pendingActions.Setup(r => r.AddAsync(It.IsAny<AssistantPendingAction>(), true, It.IsAny<CancellationToken>()))
            .Callback<AssistantPendingAction, bool, CancellationToken>((action, _, _) => added.Add(action)).Returns(Task.CompletedTask);
        batches.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<BatchDto>>([new BatchDto(BatchId, "Cats", null, "cat lovers", "tshirt", "draft", DateTime.UnixEpoch, 0)]));
    }

    private static AssistantToolContext Context() =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller, new AssistantClientContextDto("/dashboard", null, null, null, null));

    private static T Stored<T>(AssistantPendingAction action) => JsonSerializer.Deserialize<T>(action.Arguments, AssistantJson.Options)!;

    private CreateWorkflowTool WorkflowTool() => new(workflows.Object, batches.Object, pendingActions.Object, new FakeTimeProvider());

    private CreateBatchTool BatchTool() => new(batches.Object, pendingActions.Object, new FakeTimeProvider());

    private AddBatchProductsTool ProductsTool() => new(batches.Object, pendingActions.Object, new FakeTimeProvider());

    [TestMethod]
    public void CreationTools_AreSellerOnly()
    {
        var admin = new AssistantToolContext(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Admin, null);

        WorkflowTool().IsAvailable(admin).Should().BeFalse();
        BatchTool().IsAvailable(admin).Should().BeFalse();
        ProductsTool().IsAvailable(admin).Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateWorkflow_WithAnOwnedBatch_PreparesItAndNamesTheBatch()
    {
        var outcome = await WorkflowTool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"name":" Cat shirts ","batchId":"{{BatchId}}","reply":"Confirm to create it."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        added.Single().Title.Should().Be("Create workflow \"Cat shirts\"");
        added.Single().Summary.Should().Contain("Cats");
        Stored<CreateWorkflowStoredArgs>(added.Single()).BatchId.Should().Be(BatchId);
    }

    [TestMethod]
    public async Task CreateWorkflow_WithAnUnknownBatch_ReturnsNotFound()
    {
        var outcome = await WorkflowTool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"name":"Flow","batchId":"{{Guid.NewGuid()}}","reply":"ok"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("batchnotfound");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateWorkflow_OnConfirm_SavesAProductInputReadingTheBatchAndLinksToIt()
    {
        var id = Guid.NewGuid();
        SaveWorkflowRequest? saved = null;
        workflows.Setup(service => service.SaveAsync(null, It.IsAny<SaveWorkflowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, SaveWorkflowRequest, CancellationToken>((_, request, _) => saved = request)
            .ReturnsAsync(Result.Success(new WorkflowResponse(id, "Cat shirts", "", 1, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new WorkflowDefinition(2, [], []))));

        var result = await WorkflowTool().ExecuteConfirmedAsync(Guid.NewGuid(), AssistantJson.SerializeToElement(new CreateWorkflowStoredArgs("Cat shirts", "", BatchId)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Payload.GetProperty("openPath").GetString().Should().Be($"/workflows?id={id}");
        var input = saved!.Definition.Nodes.Single();
        input.Type.Should().Be("product-input");
        input.Config.GetProperty("batchId").GetString().Should().Be(BatchId.ToString());
    }

    [TestMethod]
    public async Task CreateBatch_RejectsAnUnknownProductType()
    {
        var outcome = await BatchTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"name":"Cats","productType":"sock","reply":"ok"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("tshirt");
    }

    [TestMethod]
    public async Task CreateBatch_OnConfirm_CreatesTheBatch()
    {
        batches.Setup(service => service.CreateAsync(It.Is<SaveBatchDto>(request => request.Name == "Cats" && request.DefaultProductType == "mug"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BatchDto(BatchId, "Cats", null, null, "mug", "draft", DateTime.UnixEpoch, 0)));

        await BatchTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"name":"Cats","productType":"mug","reply":"ok"}"""), default);
        var result = await BatchTool().ExecuteConfirmedAsync(Guid.NewGuid(), JsonDocument.Parse(added.Single().Arguments).RootElement, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Payload.GetProperty("batchId").GetGuid().Should().Be(BatchId);
    }

    [TestMethod]
    public async Task AddBatchProducts_FillsTheBatchDefaultsAndReportsMissingFields()
    {
        var outcome = await ProductsTool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""
            {"batchId":"{{BatchId}}","products":[{"name":"Grumpy cat tee","keywords":["cat","funny"]},{"name":"Bare","productType":"mug","niche":"cats","keywords":[]}],"reply":"ok"}
            """), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("Bare: at least one keyword is required");

        added.Clear();
        await ProductsTool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""
            {"batchId":"{{BatchId}}","products":[{"name":"Grumpy cat tee","keywords":["cat","funny"]}],"reply":"ok"}
            """), default);

        var product = Stored<AddBatchProductsStoredArgs>(added.Single()).Products.Single();
        product.ProductType.Should().Be("tshirt");
        product.Niche.Should().Be("cat lovers");
        added.Single().Title.Should().Be("Add 1 product to \"Cats\"");
    }

    [TestMethod]
    public async Task AddBatchProducts_OnConfirm_AddsEachAndListsTheOnesThatFailed()
    {
        var ok = new SaveProductDto("A", "tshirt", "cats", ["cat"], null, null);
        var bad = new SaveProductDto("B", "tshirt", "cats", ["cat"], null, null);
        batches.Setup(service => service.AddProductAsync(BatchId, It.Is<SaveProductDto>(product => product.Name == "A"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductDto(Guid.NewGuid(), BatchId, "A", "tshirt", "cats", ["cat"], null, null, "pending", null)));
        batches.Setup(service => service.AddProductAsync(BatchId, It.Is<SaveProductDto>(product => product.Name == "B"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductDto>(Error.Validation("Limit reached.")));

        var result = await ProductsTool().ExecuteConfirmedAsync(Guid.NewGuid(), AssistantJson.SerializeToElement(new AddBatchProductsStoredArgs(BatchId, [ok, bad])), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Be("1 products added; not added: B (Limit reached.).");
    }
}

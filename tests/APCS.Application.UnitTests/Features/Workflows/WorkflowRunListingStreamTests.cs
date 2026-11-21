using System.Text.Json;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRunListingStreamTests
{
    private readonly WorkflowTestData fixture = new();
    private readonly Guid streamId = Guid.NewGuid();

    [TestMethod]
    public async Task StartAsync_WithAListingStream_StreamsTheListingThenEndsTheStream()
    {
        var workflow = AddListingOnlyWorkflow(fixture.AddProduct());
        ListingIsWrittenThroughTheObserver();

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "listing-stream", streamId), default);

        result.Value.Status.Should().Be("completed");
        StreamTypes().Should().Equal("start", "done", "end");
    }

    [TestMethod]
    public async Task StartAsync_WithAListingStreamOnAStaleWorkflow_StillEndsTheStream()
    {
        var workflow = AddListingOnlyWorkflow(fixture.AddProduct());

        var result = await fixture.RunService.StartAsync(workflow.Id, new(3, "stale", streamId), default);

        result.Error.Code.Should().Be("StaleRevision");
        StreamTypes().Should().Equal("end");
    }

    [TestMethod]
    public async Task StartAsync_WithAListingStreamAndNoListingStep_OnlyEndsTheStream()
    {
        var workflow = fixture.AddWorkflow(fixture.AddProduct());

        await fixture.RunService.StartAsync(workflow.Id, new(4, "video-only", streamId), default);

        StreamTypes().Should().Equal("end");
        fixture.ListingContent.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartAsync_WhenTheListingThrows_StillEndsTheStream()
    {
        var workflow = AddListingOnlyWorkflow(fixture.AddProduct());
        fixture.ListingContent
            .Setup(x => x.GetOrGenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<IListingGenerationObserver?>()))
            .ThrowsAsync(new TimeoutException("Database timeout."));

        var start = () => fixture.RunService.StartAsync(workflow.Id, new(4, "throws", streamId), default);

        await start.Should().ThrowAsync<TimeoutException>();
        StreamTypes().Should().Equal("end");
    }

    [TestMethod]
    public async Task StartAsync_WithoutAListingStream_PassesNoObserver()
    {
        var workflow = AddListingOnlyWorkflow(fixture.AddProduct());
        fixture.ListingSucceeds();

        await fixture.RunService.StartAsync(workflow.Id, new(4, "no-stream"), default);

        fixture.ListingContent.Verify(x => x.GetOrGenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), null), Times.Once);
        fixture.ListingStreams.Entries(WorkflowTestData.Owner, streamId).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_RetryListingWithAListingStream_StreamsTheListingThenEndsTheStream()
    {
        var run = fixture.AddListingOnlyRun(fixture.AddProduct(), "failed");
        fixture.NodeRows.Single(x => x.NodeType == "generate-listing").Status = "failed";
        ListingIsWrittenThroughTheObserver();

        var result = await fixture.RunService.ActionAsync(run.Id, new(run.Revision, "retry_listing", ListingStreamId: streamId), default);

        result.Value.Status.Should().Be("completed");
        StreamTypes().Should().Equal("start", "done", "end");
    }

    [TestMethod]
    public async Task ActionAsync_RejectedWithAListingStream_StillEndsTheStream()
    {
        var run = fixture.AddListingOnlyRun(fixture.AddProduct(), "failed");

        var result = await fixture.RunService.ActionAsync(run.Id, new(run.Revision - 1, "retry_listing", ListingStreamId: streamId), default);

        result.IsFailure.Should().BeTrue();
        StreamTypes().Should().Equal("end");
    }

    private IEnumerable<string> StreamTypes() =>
        fixture.ListingStreams.Entries(WorkflowTestData.Owner, streamId).Select(entry => entry.Type);

    private void ListingIsWrittenThroughTheObserver() => fixture.ListingContent
        .Setup(x => x.GetOrGenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsNotNull<IListingGenerationObserver>()))
        .Returns(async (Guid productId, CancellationToken token, IListingGenerationObserver? observer) =>
        {
            var listing = new ListingContentResponseDto(productId, Guid.NewGuid(), "Vintage road trip tee", "A soft tee.", [], 1, "text-model", WorkflowTestData.Now.UtcDateTime);
            await observer!.StartedAsync("text-model", token);
            await observer.CompletedAsync(listing, token);
            return Result.Success(listing);
        });

    private Workflow AddListingOnlyWorkflow(Product product)
    {
        var input = JsonSerializer.SerializeToElement<object>(new ProductInputConfig(product.BatchId, product.Id), WorkflowJson.Options);
        var definition = new WorkflowDefinition(2,
            [new("input", "product-input", "Product input", new(0, 0), input), new("listing", "generate-listing", "Listing content", new(200, 0), JsonSerializer.SerializeToElement(new { }))],
            [new("edge", "input", "listing")]);
        var workflow = new Workflow { Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, Name = "Listing workflow", Description = "",
            Revision = 4, SchemaVersion = 2, Definition = WorkflowJson.Write(definition),
            CreatedAt = WorkflowTestData.Now.UtcDateTime, UpdatedAt = WorkflowTestData.Now.UtcDateTime };
        fixture.WorkflowRows.Add(workflow);
        return workflow;
    }
}

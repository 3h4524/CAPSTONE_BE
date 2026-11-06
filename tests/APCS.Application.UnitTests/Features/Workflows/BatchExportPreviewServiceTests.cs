using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class BatchExportPreviewServiceTests
{
    [TestMethod]
    public async Task PreviewAsync_MultipleProducts_ReturnsOnlyTheirApprovedAssetsAndReportsMissingContent()
    {
        var owner = Guid.NewGuid(); var batchId = Guid.NewGuid(); var otherBatch = Guid.NewGuid();
        var first = new Product { Id = Guid.NewGuid(), UserId = owner, BatchId = batchId, Name = "First", ProductType = "tshirt" };
        var second = new Product { Id = Guid.NewGuid(), UserId = owner, BatchId = batchId, Name = "Second", ProductType = "mug" };
        var outside = new Product { Id = Guid.NewGuid(), UserId = owner, BatchId = otherBatch, Name = "Other", ProductType = "mug" };
        var approved = WorkflowTestData.Asset(first.Id); approved.StorageKey = "private/first"; approved.StorageVersion = "1";
        var stale = WorkflowTestData.Asset(first.Id); stale.ApprovedRevision--; stale.StorageKey = "private/stale";
        var foreign = WorkflowTestData.Asset(outside.Id); foreign.StorageKey = "private/other";
        var current = new Mock<ICurrentUser>(); current.SetupGet(x => x.UserId).Returns(owner); current.SetupGet(x => x.IsAuthenticated).Returns(true);
        var batchRows = Rows(new[] { new Batch { Id = batchId, UserId = owner, Name = "Batch" } }, item => item.Id);
        var productRows = Rows(new[] { first, second, outside }, item => item.Id);
        var mockups = Rows(new[] { approved, stale, foreign }, item => item.Id);
        var designs = Rows(Array.Empty<DesignImage>(), item => item.Id);
        var videos = Rows(Array.Empty<PromoVideo>(), item => item.Id);
        var listing = new Mock<IListingContentService>();
        listing.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ListingContentResponseDto>(Error.NotFound("ListingContent.NotFound", "No listing")));
        var storage = new Mock<IMediaStorage>();
        storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), false)).Returns("https://example.invalid/preview");
        var service = new BatchExportPreviewService(current.Object, batchRows.Object, productRows.Object, designs.Object,
            mockups.Object, videos.Object, listing.Object, storage.Object);

        var result = await service.PreviewAsync(new(batchId), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Products.Select(item => item.ProductId).Should().BeEquivalentTo(new[] { first.Id, second.Id });
        result.Value.Products.Single(item => item.ProductId == first.Id).Mockups.Select(item => item.Id).Should().Equal(approved.Id);
        result.Value.Products.Single(item => item.ProductId == second.Id).Warnings.Should().Contain("No listing content.");
        storage.Verify(x => x.SignRead("private/stale", It.IsAny<string>(), It.IsAny<string?>(), false), Times.Never);
        storage.Verify(x => x.SignRead("private/other", It.IsAny<string>(), It.IsAny<string?>(), false), Times.Never);
        var rejected = await service.PreviewAsync(new(batchId, [outside.Id]), default);
        rejected.Error.Code.Should().Be("Export.ProductNotFound");
    }

    [TestMethod]
    public async Task PreviewAsync_AnotherUsersBatch_DoesNotReadItsProducts()
    {
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.UserId).Returns(Guid.NewGuid()); user.SetupGet(x => x.IsAuthenticated).Returns(true);
        var batch = new Batch { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Private" };
        var batches = Rows(new[] { batch }, item => item.Id);
        var products = new Mock<IRepository<Product>>(MockBehavior.Strict);
        var service = new BatchExportPreviewService(user.Object, batches.Object, products.Object, Mock.Of<IRepository<DesignImage>>(),
            Mock.Of<IRepository<MockupImage>>(), Mock.Of<IRepository<PromoVideo>>(), Mock.Of<IListingContentService>(), Mock.Of<IMediaStorage>());

        var result = await service.PreviewAsync(new(batch.Id), default);

        result.Error.Code.Should().Be("Export.BatchNotFound");
        products.VerifyNoOtherCalls();
    }

    private static Mock<IRepository<T>> Rows<T>(IReadOnlyList<T> rows, Func<T, Guid> id) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object key, CancellationToken _) => rows.FirstOrDefault(item => id(item) == (Guid)key));
        repository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => (IReadOnlyList<T>)rows.Where(predicate.Compile()).ToArray());
        return repository;
    }
}

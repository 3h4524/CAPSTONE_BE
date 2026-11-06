using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class ExportMockupSnapshotsTests
{
    [TestMethod]
    public async Task Capture_ReturnsAllCurrentApprovedMockupsForOnlyTheSelectedProduct()
    {
        var data = new WorkflowTestData();
        var product = data.AddProduct();
        var first = data.AddAsset(product);
        var second = data.AddAsset(product);
        var pending = data.AddAsset(product); pending.ApprovalStatus = "pending";
        var stale = data.AddAsset(product); stale.ApprovedRevision--;
        var deleted = data.AddAsset(product); deleted.DeletedAt = DateTime.UtcNow;
        var other = data.AddAsset(data.AddProduct());
        var service = new ExportMockupSnapshots(data.Assets.Object);

        var result = await service.CaptureAsync(product.Id, default);

        result.Select(item => item.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
        (await service.ValidateAsync(product.Id, result, default)).IsSuccess.Should().BeTrue();
        second.MetadataRevision++;
        (await service.ValidateAsync(product.Id, result, default)).Error.Code.Should().Be("Export.MockupChanged");
    }

    [TestMethod]
    public async Task Validate_RejectsMockupFromAnotherProduct()
    {
        var data = new WorkflowTestData();
        var first = data.AddProduct(); var second = data.AddProduct();
        data.AddAsset(first);
        var service = new ExportMockupSnapshots(data.Assets.Object);
        var snapshot = await service.CaptureAsync(first.Id, default);

        (await service.ValidateAsync(second.Id, snapshot, default)).Error.Code.Should().Be("Export.MockupChanged");
    }
}

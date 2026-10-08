using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoArtifactServiceTests
{
    [TestMethod]
    public async Task ListAsync_OwnVersionedVideos_ReturnsNewestFirstWithOrderedScenesAndSignedPreviews()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var first = fixture.VideoRows.Single();
        var board = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value;
        first.ConfigSnapshot = WorkflowJson.Write(new { config = new GenerateVideoConfig(), storyboard = board }); first.QaResult = "{}";
        var originalScene = fixture.SceneRows.Single(); originalScene.SceneConfig = WorkflowJson.Write(board.Scenes[0]);
        var newest = new PromoVideo { Id = Guid.NewGuid(), WorkflowRunId = run.Id, ProductId = run.ProductId, Mode = "standard", VersionNumber = 2,
            ReviewRevision = 1, Status = "queued", ApprovalStatus = "pending", ConfigSnapshot = first.ConfigSnapshot, QaResult = "{}" };
        fixture.VideoRows.Add(newest);
        fixture.Storage.Setup(x => x.SignRead("private/approved-video", "video", "1", false)).Returns("https://example.invalid/video");
        fixture.Storage.Setup(x => x.SignRead("private/thumbnail", "image", "1", false)).Returns("https://example.invalid/thumbnail");
        var result = await fixture.ArtifactService.ListAsync(run.Id, default);
        result.Value.Select(x => x.Id).Should().Equal(newest.Id, first.Id);
        result.Value[0].PreviewUrl.Should().BeNull();
        result.Value[1].PreviewUrl.Should().Be("https://example.invalid/video");
        result.Value[1].ThumbnailUrl.Should().Be("https://example.invalid/thumbnail");
        result.Value[1].Scenes.Select(x => x.MockupId).Should().Equal(fixture.AssetRows.Single().Id);
        result.Value[1].Template.Should().Be("product_showcase");
    }

    [TestMethod]
    public async Task ListAsync_ForeignRun_DoesNotIssuePreviewUrls()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct()); run.UserId = Guid.NewGuid();
        var result = await fixture.ArtifactService.ListAsync(run.Id, default);
        result.Error.Code.Should().Be("MediaNotFound");
        fixture.Storage.Verify(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    [DataRow("foreign_owner")]
    [DataRow("not_completed")]
    [DataRow("missing_storage")]
    public async Task DownloadAsync_UnavailableVideo_DoesNotSignDownload(string reason)
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        if (reason == "foreign_owner") run.UserId = Guid.NewGuid();
        if (reason == "not_completed") video.Status = "rendering";
        if (reason == "missing_storage") video.StorageKey = null;
        var result = await fixture.ArtifactService.DownloadAsync(video.Id, false, default);
        result.Error.Code.Should().Be("MediaNotFound");
        fixture.Storage.Verify(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task DownloadAsync_OwnCompletedVideo_SignsExactVersionAsAttachment()
    {
        var fixture = new WorkflowTestData();
        WorkflowRunServiceTests.PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        fixture.Storage.Setup(x => x.SignRead(video.StorageKey!, "video", "1", true)).Returns("https://example.invalid/download");
        var result = await fixture.ArtifactService.DownloadAsync(video.Id, false, default);
        result.Value.Should().Be("https://example.invalid/download");
        fixture.Storage.Verify(x => x.SignRead("private/approved-video", "video", "1", true), Times.Once);
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("at_expiry")]
    [DataRow("preparing")]
    [DataRow("foreign_owner")]
    [DataRow("missing_storage")]
    public async Task DownloadAsync_InvalidZip_DoesNotIssueAccess(string reason)
    {
        var fixture = new WorkflowTestData();
        var package = new ExportPackage { Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, Status = "ready", StorageKey = "private/package.zip",
            ExpiresAt = WorkflowTestData.Now.AddDays(1).UtcDateTime };
        fixture.ExportRows.Add(package);
        if (reason == "expired") package.ExpiresAt = WorkflowTestData.Now.AddTicks(-1).UtcDateTime;
        if (reason == "at_expiry") package.ExpiresAt = WorkflowTestData.Now.UtcDateTime;
        if (reason == "preparing") package.Status = "preparing";
        if (reason == "foreign_owner") package.UserId = Guid.NewGuid();
        if (reason == "missing_storage") package.StorageKey = null;
        var result = await fixture.ArtifactService.DownloadAsync(package.Id, true, default);
        result.Error.Code.Should().Be("MediaNotFound");
        fixture.Storage.Verify(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task DownloadAsync_OwnReadyZip_SignsAuthenticatedRawAttachment()
    {
        var fixture = new WorkflowTestData();
        var package = new ExportPackage { Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, Status = "ready", StorageKey = "private/package.zip",
            ExpiresAt = WorkflowTestData.Now.AddDays(1).UtcDateTime };
        fixture.ExportRows.Add(package);
        fixture.Storage.Setup(x => x.SignRead("private/package.zip", "raw", null, true)).Returns("https://example.invalid/zip");
        var result = await fixture.ArtifactService.DownloadAsync(package.Id, true, default);
        result.Value.Should().Be("https://example.invalid/zip");
        fixture.Storage.Verify(x => x.SignRead("private/package.zip", "raw", null, true), Times.Once);
    }
}

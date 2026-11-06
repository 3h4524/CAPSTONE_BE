using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ImageQuality;
using APCS.Application.Features.ImageQuality.Common;
using APCS.Application.Features.ImageQuality.Dtos.Request;
using APCS.Application.Features.ImageQuality.Validators;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.ImageQuality;

[TestClass]
public sealed class ImageQualityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 2, 0, 0, TimeSpan.Zero);

    private readonly Mock<IRepository<ImageEvalRun>> _runs = new();
    private readonly Mock<IRepository<DesignImage>> _designImages = new();
    private readonly Mock<IRepository<BatchJob>> _batchJobs = new();
    private readonly Mock<IRepository<BatchJobLog>> _batchJobLogs = new();
    private readonly Mock<IPublicImageService> _publicImages = new();
    private readonly List<ImageEvalRun> _added = [];
    private readonly ImageQualityService _sut;

    public ImageQualityServiceTests()
    {
        _runs.Setup(r => r.AddAsync(It.IsAny<ImageEvalRun>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ImageEvalRun, bool, CancellationToken>((run, _, _) => _added.Add(run))
            .Returns(Task.CompletedTask);
        _publicImages.Setup(p => p.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => $"https://res.cloudinary.com/demo/{key}.jpg");

        _sut = new ImageQualityService(
            _runs.Object,
            _designImages.Object,
            _batchJobs.Object,
            _batchJobLogs.Object,
            _publicImages.Object,
            new ImportImageEvaluationValidator(),
            new FakeTimeProvider(Now),
            NullLogger<ImageQualityService>.Instance);
    }

    private static ImageEvalImageRequestDto Image(string id, string version = "old", string? thumbnail = null, double? musiq = 70) =>
        new(id, version, "Coffee Then Code", "tshirt", "Coffee Then Code", null, thumbnail, "COFFEE / THEN / CODE",
            true, true, true, "", 0, true, true, true, true, musiq, 0.7, 0.7, 4.5, 5, 35, 25);

    private static ImportImageEvaluationRequestDto Request(params ImageEvalImageRequestDto[] images) =>
        new("Keyword rules · 30/10", "  WordNet keywords  ",
            [new ImageEvalVersionRequestDto("old", "Original prompt", "ideogram", "before", 0),
             new ImageEvalVersionRequestDto("new", "Keyword rules", "ideogram", "after", 1)],
            images);

    private static string Thumbnail => ImageEvalThumbnailTests.DataUrl("image/jpeg", ImageEvalThumbnailTests.Jpeg);

    [TestMethod]
    public async Task ImportEvaluationAsync_ValidRun_StoresThumbnailsAndScoresWithoutTheBase64()
    {
        var result = await _sut.ImportEvaluationAsync(
            Request(Image("old-1", thumbnail: Thumbnail), Image("new-1", "new")),
            "admin@apcs.test");

        result.IsSuccess.Should().BeTrue();
        result.Value.ImageCount.Should().Be(2);
        result.Value.ProductCount.Should().Be(1);
        result.Value.Versions.Select(version => version.ImageCount).Should().Equal(1, 1);
        result.Value.CreatedAtUtc.Should().Be(Now);

        var stored = _added.Single();
        stored.Notes.Should().Be("WordNet keywords");
        stored.UploadedBy.Should().Be("admin@apcs.test");
        stored.Report.Should().NotContain("base64");
        var report = ImageEvalReport.FromJson(stored.Report);
        report.Images[0].ThumbnailUrl.Should().Be($"https://res.cloudinary.com/demo/image-eval/{stored.Id:N}/old-1.jpg");
        report.Images[1].ThumbnailUrl.Should().BeNull();
        _publicImages.Verify(p => p.UploadImageAsync(
            It.Is<UploadFileDto>(file => file.ContentType == "image/jpeg"),
            $"image-eval/{stored.Id:N}/old-1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ImportEvaluationAsync_ImageWithAnUnlistedVersion_IsRefusedBeforeAnyUpload()
    {
        var result = await _sut.ImportEvaluationAsync(Request(Image("x-1", "test-Z", thumbnail: Thumbnail)), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        _publicImages.VerifyNoOtherCalls();
        _added.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("old 1")]
    [DataRow("old.1")]
    [DataRow("../old")]
    public async Task ImportEvaluationAsync_ImageIdThatIsNotASafeKey_IsRefused(string id)
    {
        var result = await _sut.ImportEvaluationAsync(Request(Image(id)), null);

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task ImportEvaluationAsync_ScoreThatIsNotFinite_IsRefused()
    {
        var result = await _sut.ImportEvaluationAsync(Request(Image("old-1", musiq: double.NaN)), null);

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task ImportEvaluationAsync_DuplicateImageIds_AreRefused()
    {
        var result = await _sut.ImportEvaluationAsync(Request(Image("old-1"), Image("old-1")), null);

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task ImportEvaluationAsync_ThumbnailUploadFails_RemovesTheStoredOnes_AndSavesNothing()
    {
        _publicImages.Setup(p => p.UploadImageAsync(It.IsAny<UploadFileDto>(), It.Is<string>(key => key.EndsWith("/old-2")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Cloudinary is down"));

        var result = await _sut.ImportEvaluationAsync(
            Request(Image("old-1", thumbnail: Thumbnail), Image("old-2", thumbnail: Thumbnail)),
            null);

        result.Error.Code.Should().Be(ErrorCodes.ImageEvaluationThumbnailUploadFailed);
        _added.Should().BeEmpty();
        _publicImages.Verify(p => p.DeleteImageAsync(It.Is<string>(key => key.EndsWith("/old-1")), It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    [TestMethod]
    public async Task GetEvaluationAsync_UnknownRun_IsNotFound()
    {
        var result = await _sut.GetEvaluationAsync(Guid.NewGuid());

        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(ErrorCodes.ImageEvaluationNotFound);
    }

    [TestMethod]
    public async Task GetEvaluationAsync_StoredRun_ReturnsItsImages()
    {
        await _sut.ImportEvaluationAsync(Request(Image("old-1"), Image("new-1", "new")), null);
        var stored = _added.Single();
        _runs.Setup(r => r.GetByIdAsync(stored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await _sut.GetEvaluationAsync(stored.Id);

        result.Value.Images.Select(image => image.Id).Should().Equal("old-1", "new-1");
        result.Value.Images[0].Musiq.Should().Be(70);
    }

    [TestMethod]
    public async Task ListEvaluationsAsync_ReturnsNewestFirst_WithVersions()
    {
        await _sut.ImportEvaluationAsync(Request(Image("old-1")), null);
        var older = _added.Single();
        older.CreatedAt = Now.UtcDateTime.AddDays(-1);
        var newer = new ImageEvalRun { Id = Guid.NewGuid(), Name = "Newer", Report = older.Report, CreatedAt = Now.UtcDateTime };
        _runs.Setup(r => r.Query()).Returns(new List<ImageEvalRun> { older, newer }.AsQueryable().BuildMock());

        var result = await _sut.ListEvaluationsAsync();

        result.Value.Select(run => run.Name).Should().Equal("Newer", "Keyword rules · 30/10");
        result.Value[1].Versions.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task DeleteEvaluationAsync_StoredRun_RemovesTheRowAndItsThumbnails()
    {
        await _sut.ImportEvaluationAsync(Request(Image("old-1", thumbnail: Thumbnail), Image("new-1", "new")), null);
        var stored = _added.Single();
        _runs.Setup(r => r.GetByIdAsync(stored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await _sut.DeleteEvaluationAsync(stored.Id);

        result.IsSuccess.Should().BeTrue();
        _runs.Verify(r => r.RemoveAsync(stored, true, It.IsAny<CancellationToken>()), Times.Once);
        _publicImages.Verify(p => p.DeleteImageAsync($"image-eval/{stored.Id:N}/old-1", It.IsAny<CancellationToken>()), Times.Once);
        _publicImages.Verify(p => p.DeleteImageAsync(It.Is<string>(key => key.EndsWith("/new-1")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteEvaluationAsync_UnknownRun_IsNotFound()
    {
        var result = await _sut.DeleteEvaluationAsync(Guid.NewGuid());

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [TestMethod]
    public async Task GetLiveStatsAsync_CountsImagesAndFailuresInThePeriod()
    {
        var jobId = Guid.NewGuid();
        var inPeriod = new DateTime(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);
        _designImages.Setup(r => r.Query()).Returns(new List<DesignImage>
        {
            new() { CreatedAt = inPeriod, ImageGeneratorModel = "ideogram-4-transparent", ApprovalStatus = "approved", BatchJobId = jobId, ProductId = Guid.NewGuid(), GenerationTimeSeconds = 20, ApiUsageRecord = new ApiUsageRecord { CostUsd = 0.03m } },
            new() { CreatedAt = inPeriod, ImageGeneratorModel = "ideogram-4-transparent", ApprovalStatus = "approved", BatchJobId = jobId, ProductId = Guid.NewGuid(), DeletedAt = inPeriod },
            new() { CreatedAt = inPeriod.AddDays(-30), ImageGeneratorModel = "gemini-3.1-flash-image", ApprovalStatus = "approved", ProductId = Guid.NewGuid() },
        }.AsQueryable().BuildMock());
        _batchJobLogs.Setup(r => r.Query()).Returns(new List<BatchJobLog>
        {
            new() { BatchJobId = jobId, EventType = "generation_failed", CreatedAt = inPeriod },
            new() { BatchJobId = jobId, EventType = "image_timing", CreatedAt = inPeriod },
        }.AsQueryable().BuildMock());
        _batchJobs.Setup(r => r.Query()).Returns(new List<BatchJob>
        {
            new() { Id = jobId, Config = """{"imageProvider": "ideogram", "requireApproval": true}""" },
        }.AsQueryable().BuildMock());

        var result = await _sut.GetLiveStatsAsync(new GetImageQualityLiveStatsRequestDto
        {
            FromUtc = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        });

        result.Value.GeneratedAtUtc.Should().Be(Now);
        var row = result.Value.Versions.Single();
        row.Key.Should().Be("ideogram:v1-lettering-rules");
        row.Images.Should().Be(1);
        row.FailedAttempts.Should().Be(1);
        row.ApprovalRate.Should().Be(1);
        row.AverageCostUsd.Should().Be(0.03m);
    }

    [TestMethod]
    public async Task GetLiveStatsAsync_PeriodThatEndsBeforeItStarts_IsRefused()
    {
        var result = await _sut.GetLiveStatsAsync(new GetImageQualityLiveStatsRequestDto
        {
            FromUtc = Now,
            ToUtc = Now.AddDays(-1),
        });

        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}

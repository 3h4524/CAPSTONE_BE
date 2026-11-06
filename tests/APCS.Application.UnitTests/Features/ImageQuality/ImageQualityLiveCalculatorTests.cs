using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.ImageQuality.Common;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.ImageQuality;

[TestClass]
public sealed class ImageQualityLiveCalculatorTests
{
    private static readonly DateTime OldDay = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RulesDay = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ReviewedJob = Guid.NewGuid();
    private static readonly Guid AutoApprovedJob = Guid.NewGuid();
    private static readonly Guid IdeogramJob = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Configs = new()
    {
        [ReviewedJob] = """{"requireApproval": true}""",
        [AutoApprovedJob] = """{"requireApproval": false}""",
        [IdeogramJob] = """{"imageProvider": "ideogram"}""",
    };

    private static LiveImageRow Image(
        DateTime createdAt,
        string model = "gemini-3.1-flash-image",
        string approval = "pending",
        Guid? jobId = null,
        Guid? productId = null,
        string? metadata = null,
        decimal? cost = 0.04m,
        decimal seconds = 10m) =>
        new(createdAt, model, approval, seconds, metadata, jobId ?? ReviewedJob, productId ?? Guid.NewGuid(), cost);

    [TestMethod]
    public void Calculate_GroupsByProviderAndTheVersionOfTheDay_OldestFirst()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [
                Image(RulesDay, model: "ideogram-4-transparent", jobId: IdeogramJob),
                Image(OldDay),
                Image(OldDay, model: "ideogram-4", jobId: IdeogramJob),
            ],
            [],
            Configs);

        rows.Select(row => row.Key).Should().Equal(
            "gemini:v0-original", "ideogram:v0-original", "ideogram:v1-lettering-rules");
        rows[2].Label.Should().Be("Ideogram · Rule-based lettering prompt");
        rows[2].SinceUtc.Should().Be(new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero));
        rows[2].UntilUtc.Should().Be(new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero));
        rows[0].SinceUtc.Should().BeNull();
    }

    [TestMethod]
    public void Calculate_LabelledImage_UsesItsLabel_AndCountsAsLabelled()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(OldDay, metadata: """{"promptVersion": "v3-keyword-rules", "seed": 1}""")],
            [],
            Configs);

        rows.Should().ContainSingle();
        rows[0].Key.Should().Be("gemini:v3-keyword-rules");
        rows[0].LabeledImages.Should().Be(1);
    }

    [TestMethod]
    public void Calculate_CountsTheTextCheckOfTheImagesThatHaveOne()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [
                Image(OldDay, metadata: """{"textCheck": {"ok": true, "expected": "Go", "read": "GO", "cer": 0}}"""),
                Image(OldDay, metadata: """{"textCheck": {"ok": false, "expected": "Go", "read": "GOO", "cer": 0.5}}"""),
                Image(OldDay, metadata: """{"textCheck": {"ok": true, "expected": null, "read": ""}}"""),
                Image(OldDay),
            ],
            [],
            Configs);

        rows[0].TextCheckedImages.Should().Be(3);
        rows[0].TextOkImages.Should().Be(2);
        rows[0].TextOkRate.Should().BeApproximately(2.0 / 3, 1e-9);
        rows[0].AverageCharacterErrorRate.Should().BeApproximately(0.25, 1e-9, "only the images that print a phrase have one");
    }

    [TestMethod]
    public void Calculate_NoImageWasTextChecked_LeavesTheRateEmpty()
    {
        var rows = ImageQualityLiveCalculator.Calculate([Image(OldDay)], [], Configs);

        rows[0].TextCheckedImages.Should().Be(0);
        rows[0].TextOkRate.Should().BeNull();
        rows[0].AverageCharacterErrorRate.Should().BeNull();
    }

    [TestMethod]
    public void Calculate_UnlabelledImageAfterLabellingStarted_GoesToItsOwnRow()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(DesignPromptVersions.LabelingStartedUtc.AddDays(1), metadata: "{\"seed\": 7}")],
            [],
            Configs);

        rows[0].Key.Should().Be("gemini:unlabeled");
        rows[0].Label.Should().Be("Gemini · Unlabeled (older code)");
    }

    [TestMethod]
    public void Calculate_ApprovalRate_LeavesOutAutoApprovedJobsAndPendingImages()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [
                Image(OldDay, approval: "approved"),
                Image(OldDay, approval: "approved"),
                Image(OldDay, approval: "rejected"),
                Image(OldDay, approval: "pending"),
                Image(OldDay, approval: "approved", jobId: AutoApprovedJob),
            ],
            [],
            Configs);

        var row = rows.Single();
        row.ReviewedImages.Should().Be(4);
        row.ApprovedImages.Should().Be(2);
        row.RejectedImages.Should().Be(1);
        row.ApprovalRate.Should().BeApproximately(2.0 / 3, 1e-9);
    }

    [TestMethod]
    public void Calculate_FailureRate_CountsFailedAndBlockedAttemptsAgainstEveryAttempt()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(OldDay), Image(OldDay)],
            [
                new LiveLogRow(ReviewedJob, ImageQualityLiveCalculator.GenerationFailed, OldDay),
                new LiveLogRow(ReviewedJob, ImageQualityLiveCalculator.UploadFailed, OldDay),
                new LiveLogRow(ReviewedJob, ImageQualityLiveCalculator.GenerationBlocked, OldDay),
            ],
            Configs);

        var row = rows.Single();
        row.FailedAttempts.Should().Be(2);
        row.BlockedAttempts.Should().Be(1);
        row.FailureRate.Should().BeApproximately(3.0 / 5, 1e-9);
    }

    [TestMethod]
    public void Calculate_LogEvent_TakesItsProviderFromTheJobSettings()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [],
            [new LiveLogRow(IdeogramJob, ImageQualityLiveCalculator.GenerationFailed, OldDay)],
            Configs);

        rows.Single().Key.Should().Be("ideogram:v0-original");
        rows.Single().Images.Should().Be(0);
        rows.Single().FailureRate.Should().Be(1);
    }

    [TestMethod]
    public void Calculate_RegenerateRate_IsRequestsPerProductWithDesigns()
    {
        var product = Guid.NewGuid();
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(OldDay, productId: product), Image(OldDay, productId: product), Image(OldDay)],
            [new LiveLogRow(ReviewedJob, ImageQualityLiveCalculator.RegenerateRequested, OldDay)],
            Configs);

        rows.Single().Products.Should().Be(2);
        rows.Single().RegenerateRate.Should().Be(0.5);
    }

    [TestMethod]
    public void Calculate_TimeAndCost_AverageOverSavedImages_AndSkipImagesWithoutCost()
    {
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(OldDay, seconds: 10m, cost: 0.04m), Image(OldDay, seconds: 20m, cost: null)],
            [],
            Configs);

        var row = rows.Single();
        row.AverageGenerationSeconds.Should().Be(15);
        row.AverageCostUsd.Should().Be(0.04m);
        row.TotalCostUsd.Should().Be(0.04m);
        row.Jobs.Should().Be(1);
    }

    [TestMethod]
    public void Calculate_BrokenJson_IsTreatedAsMissing()
    {
        var job = Guid.NewGuid();
        var rows = ImageQualityLiveCalculator.Calculate(
            [Image(OldDay, jobId: job, metadata: "{not json", approval: "approved")],
            [],
            new Dictionary<Guid, string> { [job] = "{broken" });

        rows.Single().Key.Should().Be("gemini:v0-original");
        rows.Single().ApprovalRate.Should().Be(1);
    }
}

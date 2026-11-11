using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoAutoApprovalTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Owner = Guid.NewGuid();

    private static PromoVideo Video(MediaQa qa, string status = "completed", string approval = "pending") => new()
    {
        Id = Guid.NewGuid(), Status = status, ApprovalStatus = approval, ReviewRevision = 1,
        QaResult = WorkflowJson.Write(new { technical = qa, etsyCompliance = EtsyVideoCompliance.Evaluate(qa, "9:16", "mp4") }),
    };

    [TestMethod]
    public void Approve_FinishedListableVideoWithEtsyWarnings_IsApprovedByTheOwnerAtANewReviewRevision()
    {
        // 9:16 is an Etsy warning (it may be cropped), not a failure.
        var video = Video(WorkflowTestData.Qa());

        VideoAutoApproval.Approve(video, Owner, Now).Should().BeTrue();

        video.ApprovalStatus.Should().Be("approved");
        video.ReviewRevision.Should().Be(2);
        video.ApprovedBy.Should().Be(Owner);
        video.ApprovedAt.Should().Be(Now);
    }

    [TestMethod]
    [DataRow("too_long")]
    [DataRow("not_decoded")]
    public void Approve_VideoThatCannotBeListed_WaitsForTheSeller(string problem)
    {
        var qa = problem == "too_long" ? WorkflowTestData.Qa() with { DurationSeconds = 30 } : WorkflowTestData.Qa() with { FullDecode = false };
        var video = Video(qa);

        VideoAutoApproval.Approve(video, Owner, Now).Should().BeFalse();

        video.ApprovalStatus.Should().Be("pending");
        video.ReviewRevision.Should().Be(1);
    }

    [TestMethod]
    public void Approve_WithoutSavedChecksOrAlreadyDecided_LeavesTheVideoAsItIs()
    {
        var noChecks = new PromoVideo { Id = Guid.NewGuid(), Status = "completed", ApprovalStatus = "pending", QaResult = "{}" };
        var rejected = Video(WorkflowTestData.Qa(), approval: "rejected");
        var rendering = Video(WorkflowTestData.Qa(), status: "running");

        VideoAutoApproval.Approve(noChecks, Owner, Now).Should().BeFalse();
        VideoAutoApproval.Approve(rejected, Owner, Now).Should().BeFalse();
        VideoAutoApproval.Approve(rendering, Owner, Now).Should().BeFalse();
        rejected.ApprovalStatus.Should().Be("rejected");
    }
}

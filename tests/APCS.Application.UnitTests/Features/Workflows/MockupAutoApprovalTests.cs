using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class MockupAutoApprovalTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Owner = Guid.NewGuid();

    private static MockupImage Row(string status = "pending", bool stored = true) => new()
    {
        Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), SourceType = MockupSources.Uploaded, ApprovalStatus = status,
        MetadataRevision = 4, ContentHash = stored ? "hash" : null, Regions = "{}", CreatedAt = Now,
    };

    [TestMethod]
    public void Approve_MockupsWaitingForReview_AreApprovedAtTheirCurrentRevisionByTheOwner()
    {
        var waiting = Row();

        MockupAutoApproval.Approve([waiting], Owner, Now).Should().Be(1);

        waiting.ApprovalStatus.Should().Be("approved");
        waiting.ApprovedRevision.Should().Be(4);
        waiting.ApprovedBy.Should().Be(Owner);
        waiting.ApprovedAt.Should().Be(Now);
    }

    [TestMethod]
    public void Approve_WhatTheSellerDecidedOrCannotBeReviewed_IsLeftAsItIs()
    {
        var rejected = Row("rejected");
        var notStored = Row(stored: false);

        MockupAutoApproval.Approve([rejected, notStored], Owner, Now).Should().Be(0);

        rejected.ApprovalStatus.Should().Be("rejected");
        notStored.ApprovalStatus.Should().Be("pending");
    }

    [TestMethod]
    public void Approve_ACompositeReplacedByANewerOne_IsNotApproved()
    {
        var design = Guid.NewGuid();
        var template = Guid.NewGuid();
        MockupImage Composite(int minute) => new()
        {
            Id = Guid.NewGuid(), DesignImageId = design, MockupTemplateId = template, VariantKey = "#FFFFFF",
            SourceType = MockupSources.Generated, ApprovalStatus = "pending", MetadataRevision = 1, ContentHash = "hash",
            Regions = "{}", CreatedAt = Now.AddMinutes(minute),
        };
        var older = Composite(0);
        var newer = Composite(5);

        MockupAutoApproval.Approve([older, newer], Owner, Now);

        newer.ApprovalStatus.Should().Be("approved");
        older.ApprovalStatus.Should().Be("pending");
    }

    [TestMethod]
    public void NeedsNoConsent_StandardScenesOnly()
    {
        MockupAutoApproval.NeedsNoConsent(new GenerateVideoConfig(Mode: "standard")).Should().BeTrue();
        MockupAutoApproval.NeedsNoConsent(new GenerateVideoConfig(Mode: "ai_shot")).Should().BeFalse();
        MockupAutoApproval.NeedsNoConsent(new GenerateVideoConfig(Mode: "standard", AiScenes: [new AiVideoSceneOptions(Mode: "ai")])).Should().BeFalse();
    }
}

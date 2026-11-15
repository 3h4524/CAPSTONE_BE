using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Moderation;

[TestClass]
public sealed class ModerationApprovalTests
{
    private static readonly ModerationContext Image =
        new(Guid.NewGuid(), ModerationFeatures.DesignGeneration, ModerationDirection.Output, ModerationSubjects.DesignImage, Guid.NewGuid());

    private readonly Mock<IContentModerationService> moderation = new();
    private int reads;

    [TestMethod]
    [DataRow(null)]
    [DataRow(ModerationDecision.Allow)]
    [DataRow(ModerationDecision.Flagged)]
    public async Task EnsureApprovableAsync_CheckedOrNeverChecked_IsApprovableWithoutAnotherCheck(ModerationDecision? latest)
    {
        Latest(latest);

        (await moderation.Object.EnsureApprovableAsync(Image, Read, default)).Should().BeNull();
        reads.Should().Be(0);
        moderation.Verify(x => x.CheckImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<ModerationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EnsureApprovableAsync_Unchecked_IsCheckedAgainAndApprovedWhenItPasses()
    {
        Latest(ModerationDecision.Unchecked);
        Rechecked(ModerationVerdict.Allowed(ModerationDeciders.Gemini));

        (await moderation.Object.EnsureApprovableAsync(Image, Read, default)).Should().BeNull();
        reads.Should().Be(1);
    }

    [TestMethod]
    public async Task EnsureApprovableAsync_UncheckedAndStillUnavailable_StaysPending()
    {
        Latest(ModerationDecision.Unchecked);
        Rechecked(ModerationVerdict.NotChecked("down"));

        (await moderation.Object.EnsureApprovableAsync(Image, Read, default))!.Code.Should().Be(ModerationErrors.ModerationPendingCode);
    }

    [TestMethod]
    public async Task EnsureApprovableAsync_UncheckedAndNowBlocked_IsRefused()
    {
        Latest(ModerationDecision.Unchecked);
        Rechecked(new ModerationVerdict(ModerationDecision.Block,
            [new ModerationFinding(ModerationCategories.Sexual, ModerationSeverity.High)], ModerationDeciders.Gemini));

        (await moderation.Object.EnsureApprovableAsync(Image, Read, default))!.Code.Should().Be(ModerationErrors.ContentPolicyViolationCode);
    }

    [TestMethod]
    public async Task EnsureApprovableAsync_UncheckedAndUnreadable_StaysPending()
    {
        Latest(ModerationDecision.Unchecked);

        (await moderation.Object.EnsureApprovableAsync(Image, _ => throw new HttpRequestException("gone"), default))!
            .Code.Should().Be(ModerationErrors.ModerationPendingCode);
        (await moderation.Object.EnsureApprovableAsync(Image, null, default))!.Code.Should().Be(ModerationErrors.ModerationPendingCode);
    }

    private Task<byte[]> Read(CancellationToken _)
    {
        reads++;
        return Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private void Latest(ModerationDecision? decision) =>
        moderation.Setup(x => x.GetLatestOutputDecisionAsync(Image.SubjectType, Image.SubjectId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(decision);

    private void Rechecked(ModerationVerdict verdict) =>
        moderation.Setup(x => x.CheckImageAsync(It.IsAny<byte[]>(), "image/png", Image, It.IsAny<CancellationToken>()))
            .ReturnsAsync(verdict);
}

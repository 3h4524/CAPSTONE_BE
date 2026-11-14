using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Moderation;

[TestClass]
public sealed class ModerationPolicyTests
{
    [TestMethod]
    public void Decide_MinorsInASexualContext_BlocksAtAnySeverity()
    {
        ModerationPolicy.Decide([new(ModerationCategories.SexualMinors, ModerationSeverity.Low)])
            .Should().Be(ModerationDecision.Block);
    }

    [TestMethod]
    [DataRow(ModerationSeverity.High, ModerationDecision.Block)]
    [DataRow(ModerationSeverity.Medium, ModerationDecision.Flagged)]
    [DataRow(ModerationSeverity.Low, ModerationDecision.Allow)]
    public void Decide_OtherCategories_FollowTheSeverity(ModerationSeverity severity, ModerationDecision expected)
    {
        ModerationPolicy.Decide([new(ModerationCategories.ViolenceGore, severity)]).Should().Be(expected);
    }

    [TestMethod]
    public void Decide_TheWorstFindingWins()
    {
        ModerationPolicy.Decide(
            [
                new(ModerationCategories.ViolenceGore, ModerationSeverity.Medium),
                new(ModerationCategories.Hate, ModerationSeverity.High)
            ])
            .Should().Be(ModerationDecision.Block);
        ModerationPolicy.Decide([]).Should().Be(ModerationDecision.Allow);
    }

    [TestMethod]
    public void Decide_CategorySetToBlockFromMedium_BlocksMediumOnlyInThatCategory()
    {
        var settings = ModerationSettingsSnapshot.From(new Dictionary<string, string> { ["block_at.sexual"] = "medium" });

        ModerationPolicy.Decide([new(ModerationCategories.Sexual, ModerationSeverity.Medium)], settings)
            .Should().Be(ModerationDecision.Block);
        ModerationPolicy.Decide([new(ModerationCategories.Hate, ModerationSeverity.Medium)], settings)
            .Should().Be(ModerationDecision.Flagged);
        ModerationPolicy.Decide([new(ModerationCategories.Sexual, ModerationSeverity.Low)], settings)
            .Should().Be(ModerationDecision.Allow);
    }

    [TestMethod]
    public void SettingsSnapshot_MissingOrUnreadableValues_KeepTheDefaults()
    {
        var settings = ModerationSettingsSnapshot.From(new Dictionary<string, string>
        {
            ["enabled"] = "maybe",
            ["block_at.hate"] = "low",
            ["block_at.sexual_minors"] = "high"
        });

        settings.Enabled.Should().BeTrue();
        settings.BlockFrom(ModerationCategories.Hate).Should().Be(ModerationSeverity.High);
        settings.BlockFrom(ModerationCategories.SexualMinors).Should().Be(ModerationSeverity.Low, "minors always block");
        settings.Fingerprint.Should().Be(ModerationSettingsSnapshot.Default.Fingerprint);
        ModerationSettingsSnapshot.From(new Dictionary<string, string> { ["enabled"] = "false" }).Fingerprint
            .Should().NotBe(ModerationSettingsSnapshot.Default.Fingerprint);
    }

    [TestMethod]
    public void BlockedMessage_NamesTheCategoryAndTheField()
    {
        var verdict = new ModerationVerdict(ModerationDecision.Block,
            [new(ModerationCategories.Hate, ModerationSeverity.High, "the design text", "slur")], ModerationDeciders.Gemini);

        ModerationPolicy.BlockedMessage(verdict).Should().Contain("hateful content").And.Contain("(in the design text)");
    }
}

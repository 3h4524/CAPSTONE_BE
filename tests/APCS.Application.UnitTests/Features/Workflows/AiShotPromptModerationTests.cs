using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiShotPromptModerationTests
{
    [TestMethod]
    public async Task CheckSellerTextsAsync_ChecksOnlyTheSellersSceneText()
    {
        var moderation = new Mock<IContentModerationService>();
        IReadOnlyList<ModerationText>? checkedTexts = null;
        moderation.Setup(x => x.CheckTextAsync(It.IsAny<IReadOnlyList<ModerationText>>(), It.IsAny<ModerationContext>(), It.IsAny<CancellationToken>(), false))
            .Callback<IReadOnlyList<ModerationText>, ModerationContext, CancellationToken, bool>((texts, _, _, _) => checkedTexts = texts)
            .ReturnsAsync(ModerationVerdict.Allowed(ModerationDeciders.Gemini));
        var config = new GenerateVideoConfig(Mode: "ai_shot",
            AiScenes: [new AiVideoSceneOptions(TemplateCode: "cinematic_product_showcase", CustomPrompt: "A slow turn on a wooden table.")]);

        var blocked = await moderation.Object.CheckSellerTextsAsync(config, Guid.NewGuid(), ModerationSubjects.AiShot, null, default);

        blocked.Should().BeNull();
        checkedTexts.Should().Equal(new ModerationText("scene 1 custom prompt", "A slow turn on a wooden table."));
    }

    [TestMethod]
    public async Task CheckSellerTextsAsync_WithoutSellerText_ChecksNothing()
    {
        var moderation = new Mock<IContentModerationService>(MockBehavior.Strict);

        var blocked = await moderation.Object.CheckSellerTextsAsync(new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new AiVideoSceneOptions()]),
            Guid.NewGuid(), ModerationSubjects.AiShot, null, default);

        blocked.Should().BeNull();
    }

    [TestMethod]
    public async Task CheckSellerTextsAsync_BlockedSceneText_NamesTheField()
    {
        var moderation = new Mock<IContentModerationService>();
        moderation.Setup(x => x.CheckTextAsync(It.IsAny<IReadOnlyList<ModerationText>>(), It.IsAny<ModerationContext>(), It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new ModerationVerdict(ModerationDecision.Block,
                [new ModerationFinding(ModerationCategories.ViolenceGore, ModerationSeverity.High, "scene 1 custom prompt")], ModerationDeciders.Gemini));
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new AiVideoSceneOptions(CustomPrompt: "something violent")]);

        var blocked = await moderation.Object.CheckSellerTextsAsync(config, Guid.NewGuid(), ModerationSubjects.AiShot, null, default);

        blocked!.Code.Should().Be("ContentPolicyViolation");
        blocked.Message.Should().Contain("(in scene 1 custom prompt)");
    }
}

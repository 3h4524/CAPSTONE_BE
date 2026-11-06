using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoExperienceContextServiceTests
{
    [TestMethod]
    public async Task RetrieveForScenesAsync_VerifiedMatches_UsesOwnerScopedSearchAndCapsLessonsAndCharacters()
    {
        var retriever = new Mock<IVideoContextRetriever>();
        var first = new string('a', 700);
        var tooLargeForRemainingBudget = new string('b', 850);
        var third = new string('c', 500);
        retriever.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<VideoExperienceSearchRequest>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Match(first, "1"), Match(tooLargeForRemainingBudget, "2"), Match(third, "3"), Match("fourth", "4")
            ]);
        var product = new WorkflowTestData().AddProduct("tshirt");
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [
            new(TemplateCode: "lifestyle_in_use", PrimaryAction: "touch the collar"),
        ]);

        var result = await new VideoExperienceContextService(retriever.Object)
            .RetrieveForScenesAsync(WorkflowTestData.Owner, product, config);

        result.Should().ContainKey(0);
        var lessons = result[0];
        lessons.Should().HaveCount(3);
        lessons.Select(item => item.Content).Should().Equal(first, third, "fourth");
        lessons.Sum(item => item.Content.Length).Should().BeLessThanOrEqualTo(1500);
        retriever.Verify(x => x.SearchAsync(It.IsAny<string>(), It.Is<VideoExperienceSearchRequest>(scope =>
            scope.OwnerId == WorkflowTestData.Owner && scope.TemplateCode == "lifestyle_in_use" &&
            scope.ModelKey == AiVideoModel.ModelKey && scope.PrimaryAction == "touch the collar"), 3,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RetrieveForScenesAsync_RetrievalFails_ReturnsRuleOnlyFallback()
    {
        var retriever = new Mock<IVideoContextRetriever>();
        retriever.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<VideoExperienceSearchRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector service unavailable"));
        var product = new WorkflowTestData().AddProduct();
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new(TemplateCode: "lifestyle_in_use")]);

        var result = await new VideoExperienceContextService(retriever.Object)
            .RetrieveForScenesAsync(WorkflowTestData.Owner, product, config);

        result.Should().BeEmpty();
    }

    private static RagSearchResultDto Match(string content, string version) => new(Guid.NewGuid(), Guid.NewGuid(), "Lesson",
        "video", content, null, .9, new Dictionary<string, object?>(), version);
}

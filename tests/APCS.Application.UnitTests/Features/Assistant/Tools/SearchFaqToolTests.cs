using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Query;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class SearchFaqToolTests
{
    private readonly Mock<IRagService> rag = new();

    private static AssistantToolContext Context(AssistantCaller? caller = null) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, caller ?? AssistantTestSupport.Admin, null);

    private static RagSearchResultDto Result(Guid documentId, string title, string content = "text") =>
        new(Guid.NewGuid(), documentId, title, "faq", content, null, .9, new Dictionary<string, object?>());

    [TestMethod]
    [DataRow(null, 5)]
    [DataRow(0, 1)]
    [DataRow(99, 8)]
    public async Task ExecuteAsync_SearchesFaqContentTypeAndClampsTopK(int? requested, int expected)
    {
        using var cancellation = new CancellationTokenSource();
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), cancellation.Token)).ReturnsAsync([]);
        var args = requested is null ? """{"query":"mockups"}""" : $$"""{"query":"mockups","topK":{{requested}}}""";

        await new SearchFaqTool(rag.Object).ExecuteAsync(Context(), AssistantTestSupport.Json(args), cancellation.Token);

        rag.Verify(r => r.SearchAsync(It.Is<RagSearchRequest>(q => q.ContentType == "faq" && q.TopK == expected && q.Query == "mockups"), cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenNothingMatches_ReturnsNoteAndNoCitations()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var outcome = await new SearchFaqTool(rag.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"query":"x"}"""), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.ModelResultJson.Should().Contain("No help article matched");
        outcome.Events.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_WithResults_EmitsDistinctCitationsAndClipsContent()
    {
        var documentId = Guid.NewGuid();
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Result(documentId, "A", new string('c', 3000)), Result(documentId, "A"), Result(Guid.NewGuid(), "B")]);

        var outcome = await new SearchFaqTool(rag.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"query":"x"}"""), default);

        var citations = outcome.Events.Single().Data.Should().BeOfType<AssistantCitationsEventData>().Subject.Items;
        citations.Select(item => item.Title).Should().Equal("A", "B");
        outcome.ModelResultJson.Should().NotContain(new string('c', 1600));
    }

    [TestMethod]
    [DataRow(typeof(InvalidOperationException))]
    [DataRow(typeof(HttpRequestException))]
    public async Task ExecuteAsync_WhenEmbeddingFails_ReturnsFaqUnavailable(Type exceptionType)
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType)!);

        var outcome = await new SearchFaqTool(rag.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"query":"x"}"""), default);

        outcome.ModelResultJson.Should().Contain("faq_unavailable");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutQuery_ReturnsInvalidArguments()
    {
        var outcome = await new SearchFaqTool(rag.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("{}"), default);

        outcome.ModelResultJson.Should().Contain("invalid_arguments");
        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void IsAvailable_ForSellerAndAdmin() =>
        new[] { AssistantTestSupport.Seller, AssistantTestSupport.Admin }
            .Select(caller => new SearchFaqTool(rag.Object).IsAvailable(Context(caller))).Should().AllBeEquivalentTo(true);

    [TestMethod]
    public void Declaration_AsksForAnEnglishQueryBecauseTheArticlesAreInEnglish()
    {
        var declaration = new SearchFaqTool(rag.Object).Declaration;

        declaration.Description.Should().Contain("write the query in English").And.Contain("Make the query stand alone");
        declaration.ParametersSchema.GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .Should().Contain("in English").And.Contain("standalone").And.Contain("earlier messages");
    }
}

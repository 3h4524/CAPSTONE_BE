using System.Net;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.UnitTests.Features.ListingContent;

[TestClass]
public sealed class ListingContentServiceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private readonly Product product = new() { Id = Guid.NewGuid(), UserId = Owner, Name = "Road trip tee", ProductType = "tshirt" };
    private readonly Mock<IGeminiTextGenerationProvider> generator = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();

    [TestMethod]
    [DataRow(12)]
    [DataRow(0)]
    public async Task GenerateAsync_AnswerWithoutThirteenTags_FailsWithoutSavingAnything(int tagCount)
    {
        var tags = string.Join(",", Enumerable.Range(1, tagCount).Select(i => $$"""{"value":"tag {{i}}","type":"primary"}"""));
        Answer($$"""{"title":"Road trip tee","description":"A soft tee.","tags":[{{tags}}]}""");

        var result = await Service().GenerateAsync(product.Id);

        result.Error.Code.Should().Be("ListingContent.IncompleteResponse");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_TagLongerThanEtsyAllows_FailsInsteadOfOverflowingTheColumn()
    {
        var tags = string.Join(",", Enumerable.Range(1, 12).Select(i => $$"""{"value":"tag {{i}}","type":"primary"}"""));
        Answer($$"""{"title":"Road trip tee","description":"A soft tee.","tags":[{{tags}},{"value":"vintage camping adventure","type":"long_tail"}]}""");

        var result = await Service().GenerateAsync(product.Id);

        result.Error.Code.Should().Be("ListingContent.IncompleteResponse");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_AnswerThatIsNotJson_FailsInsteadOfThrowing()
    {
        Answer("Sorry, I cannot help with that.");

        var result = await Service().GenerateAsync(product.Id);

        result.Error.Code.Should().Be("ListingContent.IncompleteResponse");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.ServiceUnavailable, "ListingContent.ProviderBusy")]
    [DataRow(HttpStatusCode.GatewayTimeout, "ListingContent.ProviderBusy")]
    [DataRow(HttpStatusCode.TooManyRequests, "ListingContent.GenerationFailed")]
    [DataRow(HttpStatusCode.BadRequest, "ListingContent.GenerationFailed")]
    public async Task GenerateAsync_GeminiRefuses_FailsWithAReasonTheSellerCanReadAndNoProviderDetail(HttpStatusCode status, string code)
    {
        generator.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Gemini text generation request failed: API key not valid.", null, status));

        var result = await Service().GenerateAsync(product.Id);

        result.Error.Code.Should().Be(code);
        result.Error.Message.Should().NotContain("API key");
    }

    [TestMethod]
    public async Task GenerateAsync_GeminiNotUsable_FailsInsteadOfThrowing()
    {
        generator.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Gemini returned an empty generation."));

        var result = await Service().GenerateAsync(product.Id);

        result.Error.Code.Should().Be("ListingContent.GenerationFailed");
    }

    [TestMethod]
    public async Task GenerateAsync_SpareTagCandidates_KeepThirteenWhenOneIsTooLong_AndAskForTheJsonShape()
    {
        var tags = string.Join(",", Enumerable.Range(1, 15).Select(i => $$"""{"value":"tag {{i}}","type":"primary"}"""));
        Answer($$"""{"title":"Road trip tee","description":"A soft tee.","tags":[{"value":"vintage camping adventure","type":"long_tail"},{{tags}}]}""");

        var result = await Service().GenerateAsync(product.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tags.Should().HaveCount(13);
        result.Value.Tags.Select(tag => tag.Value).Should().NotContain("vintage camping adventure");
        generator.Verify(x => x.GenerateAsync(It.Is<GeminiTextGenerationRequest>(request => request.ResponseSchema != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void Answer(string text) =>
        generator.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(text, "text-model", 10, 20, "request-1"));

    [TestMethod]
    public async Task GenerateAsync_OnAPlanWithoutListingContent_IsLockedBeforeGeminiIsCalled()
    {
        var starter = TestEntitlements.Plan(features: new Dictionary<string, PlanFeatureValue> { [PlanFeatures.WorkflowListing] = new(false, null) });

        var result = await Service(starter).GenerateAsync(product.Id);

        result.Error.Code.Should().Be("Plan.FeatureLocked");
        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_OnFreeWithItsListingsUsedUp_ReturnsAllowanceReached()
    {
        var free = TestEntitlements.Free();
        free.AllowancesUsedUp.Add(UsageAllowances.Listings);

        var result = await Service(free).GenerateAsync(product.Id);

        result.Error.Code.Should().Be("Plan.AllowanceReached");
    }

    private const string Answered = """{"title":"Road trip tee","description":"A soft tee.","tags":[{"value":"tag 1","type":"primary"},{"value":"tag 2","type":"primary"},{"value":"tag 3","type":"primary"},{"value":"tag 4","type":"primary"},{"value":"tag 5","type":"primary"},{"value":"tag 6","type":"primary"},{"value":"tag 7","type":"primary"},{"value":"tag 8","type":"primary"},{"value":"tag 9","type":"primary"},{"value":"tag 10","type":"primary"},{"value":"tag 11","type":"primary"},{"value":"tag 12","type":"primary"},{"value":"tag 13","type":"primary"}]}""";

    private readonly List<ListingContentEntity> listingRows = [];
    private readonly List<ListingTitle> titleRows = [];
    private readonly List<ListingDescription> descriptionRows = [];
    private readonly List<ListingTag> tagRows = [];
    private readonly List<ListingGenerationHistory> historyRows = [];
    private readonly List<DesignImage> designRows = [];

    /// <summary>A listing already written, from <paramref name="promptUsed"/>.</summary>
    private void Listed(string promptUsed, bool userEdited = false)
    {
        var listing = new ListingContentEntity { Id = Guid.NewGuid(), ProductId = product.Id, AiModelUsed = "gemini", ModelVersion = "text-model", ApprovalStatus = "pending" };
        product.ListingContent = listing;
        listingRows.Add(listing);
        titleRows.Add(new() { Id = Guid.NewGuid(), ListingContentId = listing.Id, AiGeneratedTitle = "Old title", CurrentTitle = userEdited ? "My title" : "Old title", IsUserEdited = userEdited, VersionNumber = 1 });
        descriptionRows.Add(new() { Id = Guid.NewGuid(), ListingContentId = listing.Id, AiGeneratedDescription = "Old.", CurrentDescription = "Old.", StructureFollowed = "default", VersionNumber = 1 });
        tagRows.Add(new() { Id = Guid.NewGuid(), ListingContentId = listing.Id, VersionNumber = 1 });
        historyRows.Add(new() { Id = Guid.NewGuid(), ListingContentId = listing.Id, GenerationNumber = 1, PromptUsed = promptUsed, RawAiResponse = "{}", UserAction = "generated" });
    }

    private string PromptFor(Product row) => APCS.Application.Features.ListingContent.ListingPromptBuilder.Build(new(
        row.Id, row.Name, row.ProductType, row.NicheCategory, row.TargetAudience, row.InputDescription, [], row.Notes, row.DesiredDesignText, null), [], []);

    [TestMethod]
    public async Task GenerateAsync_PutsTheDesignTextAndTheApprovedDesignsPromptInThePrompt()
    {
        product.DesiredDesignText = "Not all who wander are lost";
        designRows.Add(new() { Id = Guid.NewGuid(), ProductId = product.Id, ApprovalStatus = "approved", IsFinal = true,
            AiPrompt = new() { GeneratedPrompt = "retro sunset over pine mountains", OriginalDescription = "", SystemPrompt = "" },
            ImageGeneratorModel = "m", StorageProvider = "s", StorageKey = "k", ImageUrl = "u", FileFormat = "png" });
        Answer(Answered);

        await Service().GenerateAsync(product.Id);

        generator.Verify(x => x.GenerateAsync(It.Is<GeminiTextGenerationRequest>(request =>
            request.Prompt.Contains("Text printed on the design: Not all who wander are lost")
            && request.Prompt.Contains("Artwork: retro sunset over pine mountains")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetOrGenerateAsync_AListingWrittenFromTheProductAsItIs_IsReusedWithoutGeminiOrAllowance()
    {
        Listed(PromptFor(product));
        var free = TestEntitlements.Free();
        free.AllowancesUsedUp.Add(UsageAllowances.Listings);

        var result = await Service(free).GetOrGenerateAsync(product.Id);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Title.Should().Be("Old title");
        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetOrGenerateAsync_AListingTheSellerEdited_IsKept()
    {
        Listed("a prompt for other details", userEdited: true);

        var result = await Service().GetOrGenerateAsync(product.Id);

        result.Value.Title.Should().Be("My title");
        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetOrGenerateAsync_AProductWhoseDesignTextChanged_IsWrittenAgain()
    {
        Listed(PromptFor(product));
        product.DesiredDesignText = "Explore more";
        Answer(Answered);

        await Service().GetOrGenerateAsync(product.Id);

        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_WithAnObserver_StreamsTheAnswerThenReportsTheSavedListing()
    {
        var observer = new RecordingObserver();
        StreamAnswer(Answered[..20], Answered[20..]);

        var result = await Service().GenerateAsync(product.Id, observer: observer);

        result.IsSuccess.Should().BeTrue();
        observer.Events.Should().Equal("start:text-model", $"text:{Answered[..20]}", $"text:{Answered[20..]}", "done:Road trip tee");
        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_WithAnObserverWhenTheProviderStartsAgain_ReportsTheRestart()
    {
        var observer = new RecordingObserver();
        generator.Setup(x => x.GenerateStreamingAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<IGeminiTextStreamObserver>(), It.IsAny<CancellationToken>()))
            .Returns(async (GeminiTextGenerationRequest _, IGeminiTextStreamObserver stream, CancellationToken token) =>
            {
                await stream.AttemptStartedAsync("text-model", false, token);
                await stream.TextReceivedAsync("{\"ti", token);
                await stream.AttemptStartedAsync("fallback-model", true, token);
                await stream.TextReceivedAsync(Answered, token);
                return new GeminiTextGenerationResult(Answered, "fallback-model", 10, 20, "request-1");
            });

        await Service().GenerateAsync(product.Id, observer: observer);

        observer.Events.Should().Equal("start:text-model", "text:{\"ti", "reset", $"text:{Answered}", "done:Road trip tee");
    }

    [TestMethod]
    public async Task GenerateAsync_WithAnObserverAndAnIncompleteAnswer_ReportsTheFailure()
    {
        var observer = new RecordingObserver();
        StreamAnswer("Sorry, I cannot help with that.");

        var result = await Service().GenerateAsync(product.Id, observer: observer);

        result.Error.Code.Should().Be("ListingContent.IncompleteResponse");
        observer.Events.Last().Should().Be($"failed:{result.Error.Message}");
    }

    [TestMethod]
    public async Task GenerateAsync_WithAnObserverOnALockedPlan_ReportsTheFailureWithoutStreaming()
    {
        var observer = new RecordingObserver();
        var starter = TestEntitlements.Plan(features: new Dictionary<string, PlanFeatureValue> { [PlanFeatures.WorkflowListing] = new(false, null) });

        var result = await Service(starter).GenerateAsync(product.Id, observer: observer);

        observer.Events.Should().Equal($"failed:{result.Error.Message}");
        generator.Verify(x => x.GenerateStreamingAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<IGeminiTextStreamObserver>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetOrGenerateAsync_WithAnObserverAndACurrentListing_ReportsItAtOnceWithoutGenerating()
    {
        Listed(PromptFor(product));
        var observer = new RecordingObserver();

        await Service().GetOrGenerateAsync(product.Id, observer: observer);

        observer.Events.Should().Equal("done:Old title");
        generator.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GetOrGenerateAsync_WithoutAnObserver_DoesNotStream()
    {
        Answer(Answered);

        await Service().GetOrGenerateAsync(product.Id);

        generator.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        generator.Verify(x => x.GenerateStreamingAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<IGeminiTextStreamObserver>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void StreamAnswer(params string[] chunks) =>
        generator.Setup(x => x.GenerateStreamingAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<IGeminiTextStreamObserver>(), It.IsAny<CancellationToken>()))
            .Returns(async (GeminiTextGenerationRequest _, IGeminiTextStreamObserver stream, CancellationToken token) =>
            {
                await stream.AttemptStartedAsync("text-model", false, token);
                foreach (var chunk in chunks)
                    await stream.TextReceivedAsync(chunk, token);
                return new GeminiTextGenerationResult(string.Concat(chunks), "text-model", 10, 20, "request-1");
            });

    private sealed class RecordingObserver : IListingGenerationObserver
    {
        public List<string> Events { get; } = [];

        public Task StartedAsync(string model, CancellationToken cancellationToken) => Record($"start:{model}");

        public Task TextAsync(string text, CancellationToken cancellationToken) => Record($"text:{text}");

        public Task RestartedAsync(CancellationToken cancellationToken) => Record("reset");

        public Task CompletedAsync(ListingContentResponseDto listing, CancellationToken cancellationToken) => Record($"done:{listing.Title}");

        public Task FailedAsync(string message, CancellationToken cancellationToken) => Record($"failed:{message}");

        private Task Record(string item)
        {
            Events.Add(item);
            return Task.CompletedTask;
        }
    }

    private ListingContentService Service(TestEntitlements? entitlements = null)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(Owner);
        var products = new Mock<IRepository<Product>>();
        products.Setup(x => x.Query()).Returns(new[] { product }.AsQueryable().BuildMock());
        var policy = new Mock<IPolicyContextRetriever>();
        policy.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var listing = new Mock<IListingContextRetriever>();
        listing.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<string?>())).ReturnsAsync([]);
        // Without Listed(), there is no listing yet, so generating is a first listing rather than a regeneration.
        static Mock<IRepository<T>> Rows<T>(List<T> rows) where T : class
        {
            var repository = new Mock<IRepository<T>>();
            repository.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
            return repository;
        }
        return new(user.Object, products.Object, Rows(listingRows).Object, Rows(titleRows).Object,
            Rows(descriptionRows).Object, Rows(tagRows).Object, Rows(new List<ListingTagItem>()).Object,
            Rows(historyRows).Object, Mock.Of<IRepository<SeoScore>>(), Mock.Of<IRepository<ApiUsageRecord>>(),
            Mock.Of<IRagService>(), policy.Object, listing.Object, generator.Object, unitOfWork.Object, TimeProvider.System, NullLogger<ListingContentService>.Instance,
            entitlements ?? TestEntitlements.AllowAll(), Rows(designRows).Object);
    }
}

using System.Net;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.ListingContent;
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

    private void Answer(string text) =>
        generator.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(text, "text-model", 10, 20, "request-1"));

    private ListingContentService Service()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(Owner);
        var products = new Mock<IRepository<Product>>();
        products.Setup(x => x.Query()).Returns(new[] { product }.AsQueryable().BuildMock());
        var policy = new Mock<IPolicyContextRetriever>();
        policy.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var listing = new Mock<IListingContextRetriever>();
        listing.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new(user.Object, products.Object, Mock.Of<IRepository<ListingContentEntity>>(), Mock.Of<IRepository<ListingTitle>>(),
            Mock.Of<IRepository<ListingDescription>>(), Mock.Of<IRepository<ListingTag>>(), Mock.Of<IRepository<ListingTagItem>>(),
            Mock.Of<IRepository<ListingGenerationHistory>>(), Mock.Of<IRepository<SeoScore>>(), Mock.Of<IRepository<ApiUsageRecord>>(),
            policy.Object, listing.Object, generator.Object, unitOfWork.Object, TimeProvider.System, NullLogger<ListingContentService>.Instance);
    }
}

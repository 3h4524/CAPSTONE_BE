using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class RagCachingTests
{
    private sealed class MemoryCache : ICacheService
    {
        public Dictionary<string, object> Store { get; } = [];

        public bool Fail { get; set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("redis down");
            return Task.FromResult(Store.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("redis down");
            Store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private readonly MemoryCache cache = new();
    private readonly Mock<IRagEmbeddingProvider> embedding = new();
    private readonly Mock<IRagService> rag = new();

    private static Microsoft.Extensions.Options.IOptions<RagCacheOptions> Options(bool enabled = true) =>
        Microsoft.Extensions.Options.Options.Create(new RagCacheOptions { Enabled = enabled });

    private MemoizedEmbeddingProvider Memoized(bool enabled = true)
    {
        embedding.SetupGet(e => e.ModelName).Returns("model-a");
        embedding.SetupGet(e => e.Dimensions).Returns(3);
        embedding.Setup(e => e.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1f, 2f, 3f]);
        return new MemoizedEmbeddingProvider(embedding.Object, cache, Options(enabled), NullLogger<MemoizedEmbeddingProvider>.Instance);
    }

    private CachedRagSearchService Cached(bool enabled = true) =>
        new(rag.Object, cache, Options(enabled), NullLogger<CachedRagSearchService>.Instance);

    private static RagSearchResultDto Result(string title = "A") =>
        new(Guid.NewGuid(), Guid.NewGuid(), title, "faq", "content", null, .9, new Dictionary<string, object?>());

    private static RagSearchRequest Faq(string query = "How many mockups?") => new(query, 5, ContentType: "faq");

    [TestMethod]
    public async Task Embedding_SecondCallWithEquivalentText_HitsCache()
    {
        var provider = Memoized();

        await provider.CreateEmbeddingAsync("How many   mockups?");
        var second = await provider.CreateEmbeddingAsync("  how many mockups? ");

        second.Should().Equal(1f, 2f, 3f);
        embedding.Verify(e => e.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Embedding_DifferentText_CallsProviderAgain()
    {
        var provider = Memoized();

        await provider.CreateEmbeddingAsync("one");
        await provider.CreateEmbeddingAsync("two");

        embedding.Verify(e => e.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Embedding_CachedVectorWithWrongDimensions_IsIgnored()
    {
        var provider = Memoized();
        cache.Store[RagCacheKeys.Embedding("model-a", "text")] = new float[] { 9f };

        var vector = await provider.CreateEmbeddingAsync("text");

        vector.Should().Equal(1f, 2f, 3f);
    }

    [TestMethod]
    public async Task Embedding_WhenProviderFails_DoesNotCacheTheFailure()
    {
        var provider = Memoized();
        embedding.Setup(e => e.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("429"));

        var act = () => provider.CreateEmbeddingAsync("text");

        await act.Should().ThrowAsync<HttpRequestException>();
        cache.Store.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Embedding_WhenCacheIsDown_FallsThroughToProvider()
    {
        var provider = Memoized();
        cache.Fail = true;

        var vector = await provider.CreateEmbeddingAsync("text");

        vector.Should().Equal(1f, 2f, 3f);
    }

    [TestMethod]
    public async Task Embedding_WhenDisabled_NeverTouchesCache()
    {
        var provider = Memoized(enabled: false);

        await provider.CreateEmbeddingAsync("text");
        await provider.CreateEmbeddingAsync("text");

        cache.Store.Should().BeEmpty();
        embedding.Verify(e => e.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Embedding_BlankText_IsPassedThroughForValidation()
    {
        var provider = Memoized();
        embedding.Setup(e => e.CreateEmbeddingAsync(" ", It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("empty"));

        var act = () => provider.CreateEmbeddingAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Search_FaqQueryRepeated_HitsCache()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();

        await service.SearchAsync(Faq());
        var second = await service.SearchAsync(Faq("  how MANY mockups? "));

        second.Should().ContainSingle().Which.Title.Should().Be("A");
        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("listing")]
    [DataRow("keyword")]
    [DataRow(null)]
    public async Task Search_NonSharedContentTypes_AreNeverCached(string? contentType)
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();

        await service.SearchAsync(new RagSearchRequest("q", 5, ContentType: contentType));
        await service.SearchAsync(new RagSearchRequest("q", 5, ContentType: contentType));

        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Search_FaqWithMarketFilters_IsNotCached()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();
        var request = new RagSearchRequest("q", 5, ContentType: "faq", Niche: "cats");

        await service.SearchAsync(request);
        await service.SearchAsync(request);

        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Search_DifferentTopK_UsesSeparateEntries()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();

        await service.SearchAsync(new RagSearchRequest("q", 3, ContentType: "faq"));
        await service.SearchAsync(new RagSearchRequest("q", 8, ContentType: "faq"));

        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Search_AfterIngestProcessed_StartsNewGenerationAndMisses()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();
        await service.SearchAsync(Faq());

        await service.ProcessIngestAsync(Guid.NewGuid());
        await service.SearchAsync(Faq());

        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Search_WhenIngestFails_StillStartsNewGeneration()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        rag.Setup(r => r.ProcessIngestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var service = Cached();
        await service.SearchAsync(Faq());
        var before = cache.Store[RagCacheKeys.GenerationKey];

        var act = () => service.ProcessIngestAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Store[RagCacheKeys.GenerationKey].Should().NotBe(before);
    }

    [TestMethod]
    public async Task Activate_WhenActivated_StartsNewGeneration_ButNotWhenRefused()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        var service = Cached();
        await service.SearchAsync(Faq());
        var first = cache.Store[RagCacheKeys.GenerationKey];

        rag.Setup(r => r.ActivateDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await service.ActivateDocumentAsync(Guid.NewGuid());
        cache.Store[RagCacheKeys.GenerationKey].Should().Be(first);

        rag.Setup(r => r.ActivateDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await service.ActivateDocumentAsync(Guid.NewGuid());
        cache.Store[RagCacheKeys.GenerationKey].Should().NotBe(first);
    }

    [TestMethod]
    public async Task Search_WhenCacheIsDown_FallsThroughToInnerService()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Result()]);
        cache.Fail = true;

        var results = await Cached().SearchAsync(Faq());

        results.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Search_WhenInnerThrows_DoesNotCacheTheFailure()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("429"));
        var service = Cached();

        var act = () => service.SearchAsync(Faq());

        await act.Should().ThrowAsync<HttpRequestException>();
        cache.Store.Keys.Should().NotContain(key => key.StartsWith("rag:search:") && key != RagCacheKeys.GenerationKey);
    }

    [TestMethod]
    public async Task OtherMembers_AreForwardedUnchanged()
    {
        using var cancellation = new CancellationTokenSource();
        var ingest = new RagIngestRequest("T", "faq", null, "1", "en", "c");
        var id = Guid.NewGuid();
        rag.Setup(r => r.IngestAsync(ingest, cancellation.Token)).ReturnsAsync(id);
        rag.Setup(r => r.GetAdminDocumentsAsync("ready", "s", cancellation.Token)).ReturnsAsync([]);
        var service = Cached();

        (await service.IngestAsync(ingest, cancellation.Token)).Should().Be(id);
        await service.GetAdminDocumentsAsync("ready", "s", cancellation.Token);
        await service.GetAdminDocumentAsync(id, cancellation.Token);

        rag.Verify(r => r.GetAdminDocumentAsync(id, cancellation.Token), Times.Once);
    }

    [TestMethod]
    public void CacheKeys_AreStableAndIndependentOfCaseAndSpacing()
    {
        RagCacheKeys.Embedding("m", "Hello  World").Should().Be(RagCacheKeys.Embedding("m", " hello world "));
        RagCacheKeys.Embedding("m", "hello").Should().NotBe(RagCacheKeys.Embedding("other", "hello"));
        RagCacheKeys.Search("g1", Faq()).Should().NotBe(RagCacheKeys.Search("g2", Faq()));
    }
}

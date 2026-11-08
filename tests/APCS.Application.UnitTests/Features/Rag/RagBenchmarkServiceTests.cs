using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Rag;

[TestClass]
public sealed class RagBenchmarkServiceTests
{
    private readonly Mock<IRagService> rag = new();
    private readonly Mock<ICacheService> cache = new();
    private RagEvaluationRequest? evaluated;

    [TestInitialize]
    public void Setup()
    {
        rag.Setup(x => x.EvaluateAsync(It.IsAny<RagEvaluationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RagEvaluationRequest, CancellationToken>((request, _) => evaluated = request)
            .ReturnsAsync(new RagEvaluationResponse("run", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, 0, []));
    }

    private RagBenchmarkService Service() => new(rag.Object, cache.Object, TimeProvider.System);

    private static RagKnownItemSource Listing(int index) =>
        new($"etsy:public-listing:{index}", "mom shirt", $"Title: Listing {index}\nDescription: Soft cotton tee\nTags: mom, gift, shirt\n");

    [TestMethod]
    public async Task NewRun_SamplesTheCorpusSavesTheCaseSetAndEvaluatesItAtTopTen()
    {
        rag.Setup(x => x.GetKnownItemSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 30).Select(Listing).ToArray());
        RagKnownItemCaseSet? saved = null;
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<RagKnownItemCaseSet>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagKnownItemCaseSet, TimeSpan?, CancellationToken>((_, value, _, _) => saved = value)
            .Returns(Task.CompletedTask);

        var result = await Service().RunKnownItemAsync(new RagKnownItemBenchmarkRequest(SampleSize: 10, Seed: 5));

        result!.ReusedCaseSet.Should().BeFalse();
        result.Seed.Should().Be(5);
        result.SampleSize.Should().Be(10);
        saved!.Id.Should().Be(result.CaseSetId);
        saved.Cases.Should().HaveCount(20);
        evaluated!.Cases.Should().BeEquivalentTo(saved.Cases);
        evaluated.TopK.Should().Be(RagKnownItemCaseBuilder.TopK);
    }

    [TestMethod]
    public async Task NewRun_ClampsTheSampleSize()
    {
        rag.Setup(x => x.GetKnownItemSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 100).Select(Listing).ToArray());

        var result = await Service().RunKnownItemAsync(new RagKnownItemBenchmarkRequest(SampleSize: 500, Seed: 1));

        result!.SampleSize.Should().Be(RagBenchmarkService.MaximumSampleSize);
        evaluated!.Cases.Should().HaveCount(RagBenchmarkService.MaximumSampleSize * 2);
    }

    [TestMethod]
    public async Task Rerun_EvaluatesTheSavedCaseSetWithoutSamplingAgain()
    {
        var saved = new RagKnownItemCaseSet("set-1", 9, 1, DateTimeOffset.UnixEpoch,
            RagKnownItemCaseBuilder.Build([Listing(1)], 1, 9));
        cache.Setup(x => x.GetAsync<RagKnownItemCaseSet>("rag:benchmark:known-item:set-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var result = await Service().RunKnownItemAsync(new RagKnownItemBenchmarkRequest(CaseSetId: "set-1"));

        result!.ReusedCaseSet.Should().BeTrue();
        result.CaseSetId.Should().Be("set-1");
        evaluated!.Cases.Should().BeEquivalentTo(saved.Cases);
        rag.Verify(x => x.GetKnownItemSourcesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Rerun_ReturnsNullWhenTheCaseSetIsGone()
    {
        var result = await Service().RunKnownItemAsync(new RagKnownItemBenchmarkRequest(CaseSetId: "missing"));

        result.Should().BeNull();
        rag.Verify(x => x.EvaluateAsync(It.IsAny<RagEvaluationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task NewRun_FailsClearlyWhenNoListingCanBecomeACase()
    {
        rag.Setup(x => x.GetKnownItemSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagKnownItemSource("etsy:public-listing:1", null, "Title: bare\n")]);

        var act = () => Service().RunKnownItemAsync(new RagKnownItemBenchmarkRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Translation;
using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Translations;

[TestClass]
public sealed class TranslationServiceTests
{
    [TestMethod]
    public async Task TranslateAsync_WhenProviderTranslates_StoresTimestampsWithoutKindForTimestampWithoutTimeZone()
    {
        var entries = new Mock<IRepository<TranslationEntry>>();
        entries.Setup(repository => repository.Query())
            .Returns(new List<TranslationEntry>().AsQueryable().BuildMock());
        var added = new List<TranslationEntry>();
        entries.Setup(repository => repository.AddAsync(It.IsAny<TranslationEntry>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<TranslationEntry, bool, CancellationToken>((entry, _, _) => added.Add(entry))
            .Returns(Task.CompletedTask);
        var provider = new Mock<ITranslationProvider>();
        provider.SetupGet(candidate => candidate.Name).Returns("google");
        provider.SetupGet(candidate => candidate.IsConfigured).Returns(true);
        provider
            .Setup(candidate => candidate.TranslateAsync(
                It.IsAny<IReadOnlyCollection<TranslationInput>>(), "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<TranslationInput> inputs, string _, string _, CancellationToken _) =>
                inputs.ToDictionary(input => input.SourceHash, input => $"vi:{input.SourceText}"));
        var service = new TranslationService(
            entries.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<ICacheService>(),
            provider.Object,
            new TranslateTextsValidator());

        var result = await service.TranslateAsync(
            new TranslateTextsRequestDto("en", "vi", [new TranslateTextItemDto("label", "Video")]));

        result.Value.Items.Should().ContainSingle().Which.Text.Should().Be("vi:Video");
        added.Should().ContainSingle();
        added[0].CreatedAt.Kind.Should().Be(DateTimeKind.Unspecified);
        added[0].UpdatedAt.Kind.Should().Be(DateTimeKind.Unspecified);
    }
}

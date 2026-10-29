using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.DesignTemplates;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.DesignTemplates;

[TestClass]
public sealed class DesignTemplateServiceTests
{
    private static readonly Guid SellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSellerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ListAsync_WhenSystemScopeCacheMiss_LoadsOnlySystemCatalog()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        var system = Template("Moonlit Botanicals", true, null, Now.AddMinutes(-1));
        fixture.Repository.Setup(repository => repository.ListSystemAsync(cancellation.Token))
            .ReturnsAsync([system]);

        var result = await fixture.Service.ListAsync(
            new ListDesignTemplatesRequestDto(1, 12, Scope: DesignTemplateScopes.System),
            cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("Moonlit Botanicals");
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items[0].CanEdit.Should().BeFalse();
        result.Value.Items[0].CanDelete.Should().BeFalse();
        result.Value.Items[0].CanClone.Should().BeTrue();
        fixture.Repository.Verify(repository => repository.ListSystemAsync(cancellation.Token), Times.Once);
        fixture.Repository.Verify(repository => repository.ListPersonalAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListAsync_WhenPersonalScopeCacheMiss_LoadsOnlySellerCatalog()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        var personal = Template("My Watercolor", false, SellerId, Now);
        fixture.Repository.Setup(repository => repository.ListPersonalAsync(SellerId, cancellation.Token))
            .ReturnsAsync([personal]);

        var result = await fixture.Service.ListAsync(
            new ListDesignTemplatesRequestDto(1, 12, Scope: DesignTemplateScopes.Personal),
            cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("My Watercolor");
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items[0].CanEdit.Should().BeTrue();
        result.Value.Items[0].CanDelete.Should().BeTrue();
        result.Value.Items[0].CanClone.Should().BeFalse();
        fixture.Repository.Verify(repository => repository.ListPersonalAsync(
            SellerId, cancellation.Token), Times.Once);
        fixture.Repository.Verify(repository => repository.ListSystemAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListAsync_WhenCachePayloadIsCorrupt_RemovesKeyAndRepopulatesCatalog()
    {
        var fixture = CreateFixture();
        var system = Template("System", true, null, Now);
        fixture.Cache.Setup(cache => cache.GetAsync<DesignTemplateCacheItem[]>(
                "design-templates:v1:system:all", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JsonException("Corrupt cache payload"));
        fixture.Repository.Setup(repository => repository.ListSystemAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([system]);
        fixture.Repository.Setup(repository => repository.ListPersonalAsync(
                SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await fixture.Service.ListAsync(new ListDesignTemplatesRequestDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("System");
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            "design-templates:v1:system:all",
            It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
        fixture.Repository.Verify(repository => repository.ListSystemAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ListAsync_WhenCacheConnectionFails_FallsBackWithoutRedundantRemoval()
    {
        var fixture = CreateFixture();
        var system = Template("System", true, null, Now);
        fixture.Cache.Setup(cache => cache.GetAsync<DesignTemplateCacheItem[]>(
                "design-templates:v1:system:all", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis connection unavailable"));
        fixture.Repository.Setup(repository => repository.ListSystemAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([system]);
        fixture.Repository.Setup(repository => repository.ListPersonalAsync(
                SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await fixture.Service.ListAsync(new ListDesignTemplatesRequestDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("System");
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            "design-templates:v1:system:all",
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Repository.Verify(repository => repository.ListSystemAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetAsync_WhenPersonalCatalogCacheIsStale_LoadsCurrentTemplateFromRepository()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        var currentTemplate = Template("Current template", false, SellerId, Now);
        currentTemplate.NicheCategory = DesignTemplateNiches.Outdoors;
        currentTemplate.ArtStyle = DesignTemplateArtStyles.Minimalist;
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                currentTemplate.Id, SellerId, cancellation.Token))
            .ReturnsAsync(currentTemplate);

        var result = await fixture.Service.GetAsync(currentTemplate.Id, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.NicheCategory.Should().Be(DesignTemplateNiches.Outdoors);
        result.Value.ArtStyle.Should().Be(DesignTemplateArtStyles.Minimalist);
        fixture.Repository.Verify(repository => repository.GetVisibleAsync(
            currentTemplate.Id, SellerId, cancellation.Token), Times.Once);
        fixture.Cache.Verify(cache => cache.GetAsync<DesignTemplateCacheItem[]>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetOptionsAsync_WhenPreviewGenerationIsUnavailable_DisablesCapability()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.GetOptionsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviewGenerationEnabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task ListAsync_WhenRequestCacheOperationIsCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        fixture.Cache.Setup(cache => cache.GetAsync<DesignTemplateCacheItem[]>(
                "design-templates:v1:system:all", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<DesignTemplateCacheItem[]?>(
                    new OperationCanceledException(cancellation.Token));
            });

        var action = () => fixture.Service.ListAsync(
            new ListDesignTemplatesRequestDto(),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Repository.Verify(repository => repository.ListSystemAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WithValidRequest_PersistsServerFieldsAndInvalidatesPersonalCache()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        DesignTemplate? added = null;
        fixture.Repository.Setup(repository => repository.PersonalNameExistsAsync(
                SellerId, "my template", null, cancellation.Token))
            .ReturnsAsync(false);
        fixture.Repository.Setup(repository => repository.TryAddAsync(
                It.IsAny<DesignTemplate>(), cancellation.Token))
            .Callback<DesignTemplate, CancellationToken>((template, _) => added = template)
            .ReturnsAsync(true);

        var result = await fixture.Service.CreateAsync(ValidCreate("  My   template  "), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.UserId.Should().Be(SellerId);
        added.Name.Should().Be("My template");
        added.Type.Should().Be(DesignTemplateTypes.Personal);
        added.IsSystemTemplate.Should().BeFalse();
        added.IsActive.Should().BeTrue();
        added.UsageCount.Should().Be(0);
        added.CreatedAt.Should().Be(Now.UtcDateTime);
        added.UpdatedAt.Should().Be(Now.UtcDateTime);
        added.NegativePrompt.Should().Be("logo, watermark");
        added.PreviewImageUrl.Should().BeNull();
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            $"design-templates:v1:user:{SellerId:N}:all",
            It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
        fixture.Cache.Verify(cache => cache.RemoveByPrefixAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTargetIsSystemTemplate_ReturnsForbiddenWithoutMutation()
    {
        var fixture = CreateFixture();
        var system = Template("System original", true, null, Now);
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(system.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(system);

        var result = await fixture.Service.UpdateAsync(system.Id, ValidUpdate("Changed"));

        result.Error.Code.Should().Be("design_templates.system_read_only");
        system.Name.Should().Be("System original");
        fixture.Repository.Verify(repository => repository.TrySaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTargetBelongsToAnotherSeller_ReturnsNotFound()
    {
        var fixture = CreateFixture();
        var foreign = Template("Foreign", false, OtherSellerId, Now);
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(foreign.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign);

        var result = await fixture.Service.UpdateAsync(foreign.Id, ValidUpdate("Changed"));

        result.Error.Code.Should().Be("design_templates.not_found");
        fixture.Repository.Verify(repository => repository.PersonalNameExistsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WithOwnedPersonalTemplate_UpdatesEditableFieldsOnly()
    {
        var fixture = CreateFixture();
        var originalCreatedAt = Now.AddYears(-1).UtcDateTime;
        var personal = Template("Original", false, SellerId, Now.AddYears(-1));
        personal.UsageCount = 14;
        personal.PreviewImageUrl = "/images/design-templates/existing-preview.webp";
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(
                personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personal);
        fixture.Repository.Setup(repository => repository.PersonalNameExistsAsync(
                SellerId, "updated", personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Service.UpdateAsync(personal.Id, ValidUpdate("Updated"));

        result.IsSuccess.Should().BeTrue();
        personal.Name.Should().Be("Updated");
        personal.BasePrompt.Should().Be("Create {subject} for {niche}");
        personal.UpdatedAt.Should().Be(Now.UtcDateTime);
        personal.CreatedAt.Should().Be(originalCreatedAt);
        personal.UserId.Should().Be(SellerId);
        personal.UsageCount.Should().Be(14);
        personal.IsSystemTemplate.Should().BeFalse();
        personal.PreviewImageUrl.Should().Be("/images/design-templates/existing-preview.webp");
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            $"design-templates:v1:user:{SellerId:N}:all",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenTargetIsSystemTemplate_ReturnsForbiddenWithoutSoftDelete()
    {
        var fixture = CreateFixture();
        var system = Template("System", true, null, Now);
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(system.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(system);

        var result = await fixture.Service.DeleteAsync(system.Id);

        result.Error.Code.Should().Be("design_templates.system_read_only");
        system.IsActive.Should().BeTrue();
        system.DeletedAt.Should().BeNull();
        fixture.Repository.Verify(repository => repository.IsUsedByActiveBatchAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenActiveBatchUsesTemplate_ReturnsConflictWithoutSoftDelete()
    {
        var fixture = CreateFixture();
        var personal = Template("Personal", false, SellerId, Now);
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personal);
        fixture.Repository.Setup(repository => repository.IsUsedByActiveBatchAsync(
                personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Service.DeleteAsync(personal.Id);

        result.Error.Code.Should().Be("design_templates.active_batch_reference");
        personal.IsActive.Should().BeTrue();
        personal.DeletedAt.Should().BeNull();
        fixture.Repository.Verify(repository => repository.TrySaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenEligible_SoftDeletesAndKeepsSuccessWhenCacheFails()
    {
        var fixture = CreateFixture();
        var personal = Template("Personal", false, SellerId, Now.AddDays(-1));
        fixture.Repository.Setup(repository => repository.GetForUpdateAsync(personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personal);
        fixture.Repository.Setup(repository => repository.IsUsedByActiveBatchAsync(
                personal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fixture.Cache.Setup(cache => cache.RemoveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var result = await fixture.Service.DeleteAsync(personal.Id);

        result.IsSuccess.Should().BeTrue();
        personal.IsActive.Should().BeFalse();
        personal.DeletedAt.Should().Be(Now.UtcDateTime);
        personal.UpdatedAt.Should().Be(Now.UtcDateTime);
        fixture.Repository.Verify(repository => repository.TrySaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CloneAsync_WithExistingCopy_CreatesIndependentSnapshotWithNextSuffix()
    {
        var fixture = CreateFixture();
        var source = Template("Moonlit Botanicals", true, null, Now.AddYears(-1));
        source.BasePrompt = "Create {subject}";
        source.NegativePrompt = "watermark";
        source.ExamplePrompts = "[{\"subject\":\"moth\",\"prompt\":\"moon moth\"}]";
        source.StyleDescription = "Dark botanical engraving";
        source.PreviewImageUrl = "/images/design-templates/moonlit-botanicals.webp";
        var existing = Template("moonlit botanicals copy", false, SellerId, Now.AddDays(-1));
        DesignTemplate? clone = null;
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Repository.Setup(repository => repository.ListPersonalNamesAsync(
                SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing.Name]);
        fixture.Repository.Setup(repository => repository.TryAddAsync(
                It.IsAny<DesignTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<DesignTemplate, CancellationToken>((template, _) => clone = template)
            .ReturnsAsync(true);

        var result = await fixture.Service.CloneAsync(source.Id);

        result.IsSuccess.Should().BeTrue();
        clone.Should().NotBeNull().And.NotBeSameAs(source);
        clone!.Id.Should().NotBe(source.Id);
        clone.Name.Should().Be("Moonlit Botanicals Copy 2");
        clone.UserId.Should().Be(SellerId);
        clone.Type.Should().Be(DesignTemplateTypes.Personal);
        clone.IsSystemTemplate.Should().BeFalse();
        clone.IsActive.Should().BeTrue();
        clone.UsageCount.Should().Be(0);
        clone.BasePrompt.Should().Be(source.BasePrompt);
        clone.NegativePrompt.Should().Be(source.NegativePrompt);
        clone.ExamplePrompts.Should().Be(source.ExamplePrompts);
        clone.StyleDescription.Should().Be(source.StyleDescription);
        clone.PreviewImageUrl.Should().Be(source.PreviewImageUrl);
        source.Name.Should().Be("Moonlit Botanicals");
        fixture.Repository.Verify(repository => repository.ListPersonalNamesAsync(
            SellerId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Repository.Verify(repository => repository.ListPersonalAsync(
            SellerId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CloneAsync_WhenTargetIsPersonal_ReturnsValidationWithoutWrite()
    {
        var fixture = CreateFixture();
        var personal = Template("Personal", false, SellerId, Now);
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                personal.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personal);

        var result = await fixture.Service.CloneAsync(personal.Id);

        result.Error.Code.Should().Be("validation.failed");
        fixture.Repository.Verify(repository => repository.TryAddAsync(
            It.IsAny<DesignTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CloneAsync_WhenUniqueNameRacesThreeTimes_ReturnsConflictWithoutInvalidation()
    {
        var fixture = CreateFixture();
        var source = Template("System", true, null, Now);
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Repository.Setup(repository => repository.ListPersonalNamesAsync(
                SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.Repository.Setup(repository => repository.TryAddAsync(
                It.IsAny<DesignTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Service.CloneAsync(source.Id);

        result.Error.Code.Should().Be("design_templates.clone_name_unavailable");
        fixture.Repository.Verify(repository => repository.TryAddAsync(
            It.IsAny<DesignTemplate>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CloneAsync_WhenUniqueNameRaceOccurs_RecalculatesAndRetriesWithNextSuffix()
    {
        var fixture = CreateFixture();
        var source = Template("System", true, null, Now);
        var existingCopy = Template("System Copy", false, SellerId, Now);
        var attemptedNames = new List<string>();
        var addAttempts = 0;
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Repository.SetupSequence(repository => repository.ListPersonalNamesAsync(
                SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([existingCopy.Name]);
        fixture.Repository.Setup(repository => repository.TryAddAsync(
                It.IsAny<DesignTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<DesignTemplate, CancellationToken>((template, _) => attemptedNames.Add(template.Name))
            .ReturnsAsync(() => ++addAttempts > 1);

        var result = await fixture.Service.CloneAsync(source.Id);

        result.IsSuccess.Should().BeTrue();
        attemptedNames.Should().Equal("System Copy", "System Copy 2");
        result.Value.Name.Should().Be("System Copy 2");
    }

    [TestMethod]
    public async Task ResolveForPromptAsync_WhenPromptResolves_IncrementsUsageAtomicallyAndInvalidatesOriginCache()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture();
        var source = Template("System", true, null, Now);
        source.ArtStyle = DesignTemplateArtStyles.DarkAcademia;
        source.BasePrompt = "Create {subject} for {niche} in {style} with {keywords}.";
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, cancellation.Token))
            .ReturnsAsync(source);
        fixture.Repository.Setup(repository => repository.IncrementUsageCountAsync(
                source.Id, cancellation.Token))
            .Returns(Task.CompletedTask);

        var result = await fixture.Service.ResolveForPromptAsync(
            SellerId,
            new ResolveDesignTemplatePromptRequestDto(
                source.Id,
                "luna moth",
                DesignTemplateNiches.NatureBotanical,
                null,
                ["fern", "moon phases"]),
            cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.PositivePrompt.Should().Be(
            "Create luna moth for Nature & Botanical in dark academia with fern, moon phases.");
        fixture.Repository.Verify(repository => repository.IncrementUsageCountAsync(
            source.Id, cancellation.Token), Times.Once);
        fixture.Cache.Verify(cache => cache.RemoveAsync(
            "design-templates:v1:system:all",
            It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
    }

    [TestMethod]
    public async Task ResolveForPromptAsync_WhenNegativePromptUsesPlaceholders_ResolvesThem()
    {
        var fixture = CreateFixture();
        var source = Template("System", true, null, Now);
        source.ArtStyle = DesignTemplateArtStyles.DarkAcademia;
        source.NegativePrompt = "Avoid {subject}, {style} noise, and {keywords} around {niche}.";
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        var result = await fixture.Service.ResolveForPromptAsync(
            SellerId,
            new ResolveDesignTemplatePromptRequestDto(
                source.Id,
                "luna moth",
                DesignTemplateNiches.NatureBotanical,
                null,
                ["fern"]));

        result.IsSuccess.Should().BeTrue();
        result.Value.NegativePrompt.Should().Be(
            "Avoid luna moth, dark academia noise, and fern around Nature & Botanical.");
    }

    [TestMethod]
    public async Task ResolveForPromptAsync_WhenNegativePromptIsEmpty_ReturnsNull()
    {
        var fixture = CreateFixture();
        var source = Template("System", true, null, Now);
        source.NegativePrompt = null;
        fixture.Repository.Setup(repository => repository.GetVisibleAsync(
                source.Id, SellerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        var result = await fixture.Service.ResolveForPromptAsync(
            SellerId,
            new ResolveDesignTemplatePromptRequestDto(
                source.Id,
                "luna moth",
                DesignTemplateNiches.NatureBotanical,
                null,
                []));

        result.IsSuccess.Should().BeTrue();
        result.Value.NegativePrompt.Should().BeNull();
    }

    private static Fixture CreateFixture()
    {
        var repository = new Mock<IDesignTemplateRepository>();
        var cache = new Mock<ICacheService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(SellerId);
        repository.Setup(candidate => candidate.TrySaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(candidate => candidate.IncrementUsageCountAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cache.Setup(candidate => candidate.GetAsync<DesignTemplateCacheItem[]>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DesignTemplateCacheItem[]?)null);
        cache.Setup(candidate => candidate.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new DesignTemplateService(
            repository.Object,
            cache.Object,
            currentUser.Object,
            new FakeTimeProvider(Now),
            new ListDesignTemplatesValidator(),
            new CreateDesignTemplateValidator(),
            new UpdateDesignTemplateValidator(),
            NullLogger<DesignTemplateService>.Instance);

        return new Fixture(service, repository, cache);
    }

    private static CreateDesignTemplateRequestDto ValidCreate(string name) =>
        new(
            name,
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject} for {niche} in {style} with {keywords}",
            " logo, watermark ",
            [new DesignTemplateExampleDto("iris", "A blue iris")]);

    private static UpdateDesignTemplateRequestDto ValidUpdate(string name) =>
        new(
            name,
            DesignTemplateNiches.NatureBotanical,
            DesignTemplateArtStyles.Watercolor,
            "Create {subject} for {niche}",
            null,
            []);

    private static DesignTemplate Template(
        string name,
        bool system,
        Guid? ownerId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Name = name,
            Type = system ? DesignTemplateTypes.System : DesignTemplateTypes.Personal,
            NicheCategory = DesignTemplateNiches.NatureBotanical,
            ArtStyle = DesignTemplateArtStyles.Watercolor,
            BasePrompt = "Create {subject}",
            ExamplePrompts = "[]",
            IsSystemTemplate = system,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = createdAt.UtcDateTime,
            UpdatedAt = createdAt.UtcDateTime
        };

    private sealed record Fixture(
        DesignTemplateService Service,
        Mock<IDesignTemplateRepository> Repository,
        Mock<ICacheService> Cache);
}

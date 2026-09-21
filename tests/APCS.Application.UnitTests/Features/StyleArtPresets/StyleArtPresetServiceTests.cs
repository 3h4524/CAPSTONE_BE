using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.StyleArtPresets;

[TestClass]
public sealed class StyleArtPresetServiceTests
{
    [TestMethod]
    public async Task ListMineAsync_WhenCalled_ReturnsSystemAndOwnOrderedByUsageThenName()
    {
        var userId = Guid.NewGuid();
        var repository = PresetRepository(
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Watercolor", Description = "Soft", StyleModifiers = "watercolor", Recommendations = "[\"A\"]", IsActive = true, IsSystemTemplate = true, UsageCount = 5 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Vintage", Description = "Retro", StyleModifiers = "vintage", Recommendations = "[]", IsActive = true, IsSystemTemplate = true, UsageCount = 9 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Mine", Description = "Own", StyleModifiers = "own", Recommendations = "[]", IsActive = true, IsSystemTemplate = false, UserId = userId, UsageCount = 1 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Other", Description = "Someone else", StyleModifiers = "x", Recommendations = "[]", IsActive = true, IsSystemTemplate = false, UserId = Guid.NewGuid(), UsageCount = 99 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Retired", Description = "Old", StyleModifiers = "old", Recommendations = "[]", IsActive = false, IsSystemTemplate = true, UsageCount = 99 });

        var result = await CreateService(userId: userId, repository: repository).ListMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(item => item.Name).Should().Equal("Vintage", "Watercolor", "Mine");
        result.Value.Should().OnlyContain(item => !item.IsMine || item.Name == "Mine");
        result.Value.First(item => item.Name == "Mine").IsMine.Should().BeTrue();
        result.Value.First(item => item.Name == "Vintage").IsSystemTemplate.Should().BeTrue();
    }

    [TestMethod]
    public async Task ListMineAsync_WhenUnauthenticated_ReturnsFailure()
    {
        var service = CreateService(userId: null, repository: PresetRepository());

        var result = await service.ListMineAsync();

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateAsync_WhenValid_UploadsPreviewAndReturnsMine()
    {
        var userId = Guid.NewGuid();
        var repository = PresetRepository();
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://images.example.com/preview.webp");

        var result = await CreateService(userId: userId, repository: repository, images: images).CreateAsync(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"Posters\"]", PreviewFile()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Neon");
        result.Value.PreviewImageUrl.Should().Be("https://images.example.com/preview.webp");
        result.Value.IsMine.Should().BeTrue();
        result.Value.IsSystemTemplate.Should().BeFalse();
        result.Value.Recommendations.Should().ContainSingle().Which.Should().Be("Posters");
        images.Verify(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WhenPreviewMissing_ReturnsFailureWithoutUpload()
    {
        var images = new Mock<IPublicImageService>();

        var result = await CreateService(userId: Guid.NewGuid(), repository: PresetRepository(), images: images).CreateAsync(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", null, null));

        result.IsSuccess.Should().BeFalse();
        images.Verify(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenSystemPreset_ReturnsForbidden()
    {
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StyleArtPreset { Id = presetId, Name = "Vintage", Description = "Retro", StyleModifiers = "vintage", Recommendations = "[]", IsActive = true, IsSystemTemplate = true });

        var result = await CreateService(userId: Guid.NewGuid(), repository: repository).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Vintage+", "Retro", "vintage", null, null, false));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Forbidden);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenOwnPreset_AppliesChanges()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "Old", StyleModifiers = "old", Recommendations = "[]", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        repository.Setup(r => r.Query()).Returns(new List<StyleArtPreset> { stored }.AsQueryable().BuildMock());

        var result = await CreateService(userId: userId, repository: repository).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Mine+", "New", "new", "[\"A\",\"B\"]", null, false));

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Mine+");
        result.Value.Recommendations.Should().HaveCount(2);
        repository.Verify(r => r.UpdateAsync(stored, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenOwnPresetWithPreview_RemovesRowAndImage()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "D", StyleModifiers = "m", Recommendations = "[]", PreviewImageUrl = "https://images.example.com/old.webp", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var images = new Mock<IPublicImageService>();

        var result = await CreateService(userId: userId, repository: repository, images: images).DeleteAsync(presetId);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(r => r.RemoveAsync(stored, true, It.IsAny<CancellationToken>()), Times.Once);
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenSystemPreset_ReturnsForbiddenWithoutRemove()
    {
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StyleArtPreset { Id = presetId, Name = "Vintage", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = true, IsSystemTemplate = true });

        var result = await CreateService(userId: Guid.NewGuid(), repository: repository).DeleteAsync(presetId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Forbidden);
        repository.Verify(r => r.RemoveAsync(It.IsAny<StyleArtPreset>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WhenNameTaken_ReturnsConflictWithoutUpload()
    {
        var repository = PresetRepository(
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Vintage", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = true, IsSystemTemplate = true });
        var images = new Mock<IPublicImageService>();

        var result = await CreateService(userId: Guid.NewGuid(), repository: repository, images: images).CreateAsync(
            new CreateStyleArtPresetRequestDto("vintage", "Bold glow", "neon glow", null, PreviewFile()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
        images.Verify(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WhenConcurrentDuplicate_ReturnsConflictAndDeletesImage()
    {
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.SetupSequence(r => r.Query())
            .Returns(new List<StyleArtPreset>().AsQueryable().BuildMock())
            .Returns(new List<StyleArtPreset>
            {
                new() { Id = Guid.NewGuid(), Name = "Neon", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = true, IsSystemTemplate = true }
            }.AsQueryable().BuildMock());
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Unique violation", new Exception("ux_style_art_presets_name")));
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://images.example.com/preview.webp");

        var result = await CreateService(userId: Guid.NewGuid(), repository: repository, images: images).CreateAsync(
            new CreateStyleArtPresetRequestDto("neon", "Bold glow", "neon glow", null, PreviewFile()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
        images.Verify(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WhenSaveFailsWithoutDuplicate_RethrowsAndDeletesImage()
    {
        var repository = PresetRepository();
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Connection lost", new Exception("transient")));
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://images.example.com/preview.webp");

        var act = () => CreateService(userId: Guid.NewGuid(), repository: repository, images: images).CreateAsync(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", null, PreviewFile()));

        await act.Should().ThrowAsync<DbUpdateException>();
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenConcurrentDuplicate_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "Old", StyleModifiers = "old", Recommendations = "[]", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        repository.SetupSequence(r => r.Query())
            .Returns(new List<StyleArtPreset> { stored }.AsQueryable().BuildMock())
            .Returns(new List<StyleArtPreset>
            {
                stored,
                new() { Id = Guid.NewGuid(), Name = "Taken", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = true, IsSystemTemplate = true }
            }.AsQueryable().BuildMock());
        repository.Setup(r => r.UpdateAsync(It.IsAny<StyleArtPreset>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Unique violation", new Exception("ux_style_art_presets_name")));

        var result = await CreateService(userId: userId, repository: repository).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Taken", "New description", "new", null, null, false));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenReplacePreview_UploadsWithSameKeyAndUpdatesUrl()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "Old", StyleModifiers = "old", Recommendations = "[]", PreviewImageUrl = "https://images.example.com/old.webp", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        repository.Setup(r => r.Query()).Returns(new List<StyleArtPreset> { stored }.AsQueryable().BuildMock());
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://images.example.com/new.webp");

        var result = await CreateService(userId: userId, repository: repository, images: images).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Mine", "New description", "new", null, PreviewFile(), false));

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviewImageUrl.Should().Be("https://images.example.com/new.webp");
        images.Verify(
            x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), $"style-art-presets/{presetId:N}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenDeletePreview_RemovesImageAndClearsUrl()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "Old", StyleModifiers = "old", Recommendations = "[]", PreviewImageUrl = "https://images.example.com/old.webp", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        repository.Setup(r => r.Query()).Returns(new List<StyleArtPreset> { stored }.AsQueryable().BuildMock());
        var images = new Mock<IPublicImageService>();

        var result = await CreateService(userId: userId, repository: repository, images: images).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Mine", "New description", "new", null, null, true));

        result.IsSuccess.Should().BeTrue();
        result.Value.PreviewImageUrl.Should().BeNull();
        images.Verify(x => x.DeleteImageAsync($"style-art-presets/{presetId:N}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesRowBeforeImage()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "D", StyleModifiers = "m", Recommendations = "[]", PreviewImageUrl = "https://images.example.com/old.webp", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var order = new List<string>();
        repository.Setup(r => r.RemoveAsync(stored, true, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("remove"))
            .Returns(Task.CompletedTask);
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("image"))
            .Returns(Task.CompletedTask);

        var result = await CreateService(userId: userId, repository: repository, images: images).DeleteAsync(presetId);

        result.IsSuccess.Should().BeTrue();
        order.Should().Equal("remove", "image");
    }

    [TestMethod]
    public async Task DeleteAsync_WhenImageDeleteFails_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        var stored = new StyleArtPreset { Id = presetId, Name = "Mine", Description = "D", StyleModifiers = "m", Recommendations = "[]", PreviewImageUrl = "https://images.example.com/old.webp", IsActive = true, IsSystemTemplate = false, UserId = userId };
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cloudinary unavailable"));

        var result = await CreateService(userId: userId, repository: repository, images: images).DeleteAsync(presetId);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(r => r.RemoveAsync(stored, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenUnauthenticated_ReturnsFailure()
    {
        var result = await CreateService(userId: null, repository: PresetRepository()).UpdateAsync(
            Guid.NewGuid(),
            new UpdateStyleArtPresetRequestDto("X", "Y description", "modifiers", null, null, false));

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task UpdateAsync_WhenOwnPresetInactive_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.GetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StyleArtPreset { Id = presetId, Name = "Mine", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = false, IsSystemTemplate = false, UserId = userId });

        var result = await CreateService(userId: userId, repository: repository).UpdateAsync(
            presetId,
            new UpdateStyleArtPresetRequestDto("Mine+", "New description", "new", null, null, false));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
    }

    private static StyleArtPresetService CreateService(
        Guid? userId,
        Mock<IRepository<StyleArtPreset>> repository,
        Mock<IPublicImageService>? images = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(userId.HasValue);
        currentUser.SetupGet(user => user.UserId).Returns(userId);

        return new StyleArtPresetService(
            currentUser.Object,
            repository.Object,
            (images ?? new Mock<IPublicImageService>()).Object,
            new CreateStyleArtPresetValidator(),
            new UpdateStyleArtPresetValidator(),
            new QuickCreateStyleArtPresetValidator(),
            TimeProvider.System);
    }

    private static Mock<IRepository<StyleArtPreset>> PresetRepository(params StyleArtPreset[] presets)
    {
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.Query()).Returns(presets.AsQueryable().BuildMock());
        return repository;
    }

    private static UploadFileDto PreviewFile() =>
        new("preview.webp", "image/webp", 1024, new MemoryStream([1, 2, 3]));

    [TestMethod]
    public async Task QuickCreateAsync_WhenValid_CreatesMinimalOwnPreset()
    {
        var service = CreateService(Guid.NewGuid(), PresetRepository());

        var result = await service.QuickCreateAsync("Fresh Style");

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Fresh Style");
        result.Value.IsMine.Should().BeTrue();
        result.Value.StyleModifiers.Should().BeEmpty();
    }

    [TestMethod]
    public async Task QuickCreateAsync_WhenNameTaken_ReturnsConflict()
    {
        var repository = PresetRepository(
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Vintage", Description = "D", StyleModifiers = "m", Recommendations = "[]", IsActive = true });

        var result = await CreateService(Guid.NewGuid(), repository).QuickCreateAsync("vintage");

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }
}

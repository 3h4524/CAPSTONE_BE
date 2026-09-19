using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.StyleArtPresets;

[TestClass]
public sealed class StyleArtPresetValidatorTests
{
    [TestMethod]
    public void Create_WhenValid_Passes()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"Posters\"]", PreviewFile("image/webp", 1024)));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void Create_WhenPreviewMissing_Fails()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", null, null));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Create_WhenPreviewNotImage_Fails()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", null, PreviewFile("application/pdf", 1024)));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Create_WhenPreviewOversized_Fails()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", null, PreviewFile("image/webp", StyleArtPresetRules.MaximumPreviewBytes + 1)));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Create_WhenRecommendationsInvalid_Fails()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "not-json", PreviewFile("image/webp", 1024)));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Create_WhenRecommendationsTooMany_Fails()
    {
        var result = new CreateStyleArtPresetValidator().Validate(
            new CreateStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]", PreviewFile("image/webp", 1024)));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Update_WhenPreviewAndDeletePreview_Fails()
    {
        var result = new UpdateStyleArtPresetValidator().Validate(
            new UpdateStyleArtPresetRequestDto("Mine", "New description", "new", null, PreviewFile("image/webp", 1024), true));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Update_WhenDeletePreviewOnly_Passes()
    {
        var result = new UpdateStyleArtPresetValidator().Validate(
            new UpdateStyleArtPresetRequestDto("Mine", "New description", "new", null, null, true));

        result.IsValid.Should().BeTrue();
    }

    private static UploadFileDto PreviewFile(string contentType, long length) =>
        new("preview.webp", contentType, length, new MemoryStream([1, 2, 3]));
}

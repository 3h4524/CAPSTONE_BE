using APCS.Api.Controllers;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AiStudioFrameFormRequestTests
{
    [TestMethod]
    public void TryGetConfirmedEstimatedCostUsd_OptionalOrInvariantNonNegativeValue_ParsesExpectedValue()
    {
        var form = new AiStudioFrameFormRequest();

        form.TryGetConfirmedEstimatedCostUsd(out var missing).Should().BeTrue();
        missing.Should().BeNull();

        form.ConfirmedEstimatedCostUsd = "   ";
        form.TryGetConfirmedEstimatedCostUsd(out var blank).Should().BeTrue();
        blank.Should().BeNull();

        form.ConfirmedEstimatedCostUsd = " +0.04 ";
        form.TryGetConfirmedEstimatedCostUsd(out var parsed).Should().BeTrue();
        parsed.Should().Be(0.04m);
    }

    [TestMethod]
    [DataRow("not-a-price")]
    [DataRow("-0.01")]
    [DataRow("0,04")]
    public void TryGetConfirmedEstimatedCostUsd_InvalidOrNegativeValue_IsRejected(string value)
    {
        var form = new AiStudioFrameFormRequest { ConfirmedEstimatedCostUsd = value };

        form.TryGetConfirmedEstimatedCostUsd(out var parsed).Should().BeFalse();

        parsed.Should().BeNull();
    }

    [TestMethod]
    public async Task ToRequestAsync_ReferencesExceedTwentyMegabytes_ReturnsNullWithoutOpeningFiles()
    {
        var oversized = new FormFile(Stream.Null, 0, AiStudioFrameService.MaximumSourceBytes + 1L, "reference", "large.png");
        var form = new AiStudioFrameFormRequest { References = [oversized] };

        var result = await form.ToRequestAsync(0.04m, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ToRequestAsync_ValidForm_CopiesReferenceBytesAndAllGenerationOptions()
    {
        var sourceMockupId = Guid.NewGuid();
        using var referenceContent = new MemoryStream([1, 2, 3, 4]);
        var reference = new FormFile(referenceContent, 0, 4, "reference", "moodboard.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };
        var form = new AiStudioFrameFormRequest
        {
            SourceMockupId = sourceMockupId,
            Layout = "hero",
            OutputFormat = "9:16",
            Regenerate = true,
            ExpectedFingerprint = "fingerprint-v2",
            CustomPrompt = "Soft daylight",
            TemplateCode = "cinematic_product",
            ReferenceRightsConfirmed = true,
            References = [reference],
        };

        var request = await form.ToRequestAsync(0.04m, CancellationToken.None);

        request.Should().NotBeNull();
        request!.SourceMockupId.Should().Be(sourceMockupId);
        request.Layout.Should().Be("hero");
        request.OutputFormat.Should().Be("9:16");
        request.Regenerate.Should().BeTrue();
        request.ExpectedFingerprint.Should().Be("fingerprint-v2");
        request.ConfirmedEstimatedCostUsd.Should().Be(0.04m);
        request.CustomPrompt.Should().Be("Soft daylight");
        request.TemplateCode.Should().Be("cinematic_product");
        request.ReferenceRightsConfirmed.Should().BeTrue();
        request.References.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AiStudioFrameReferenceInput([1, 2, 3, 4], "image/png"));
    }

    [TestMethod]
    public async Task QuoteStudioFrameFromForm_InvalidConfirmedCost_ReturnsBadRequestWithoutCallingTheService()
    {
        var studioFrames = new Mock<IAiStudioFrameService>(MockBehavior.Strict);
        var controller = CreateController(studioFrames.Object);

        var result = await controller.QuoteStudioFrameFromForm(Guid.NewGuid(),
            new AiStudioFrameFormRequest { ConfirmedEstimatedCostUsd = "0,04" }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        studioFrames.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GenerateStudioFrameFromForm_ReferencesOverTheLimit_ReturnsBadRequestWithoutCallingTheService()
    {
        var studioFrames = new Mock<IAiStudioFrameService>(MockBehavior.Strict);
        var controller = CreateController(studioFrames.Object);
        var oversized = new FormFile(Stream.Null, 0, AiStudioFrameService.MaximumSourceBytes + 1L, "reference", "large.png");

        var result = await controller.GenerateStudioFrameFromForm(Guid.NewGuid(),
            new AiStudioFrameFormRequest { References = [oversized] }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        studioFrames.VerifyNoOtherCalls();
    }

    private static VideoMediaController CreateController(IAiStudioFrameService studioFrames) => new(
        Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(),
        Mock.Of<ILifestyleMockupService>(), studioFrames);
}

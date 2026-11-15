using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CloudinaryPrintFileSourceTests
{
    private const string Design = "https://res.cloudinary.com/demo/image/upload/v17/apcs/design-images/a.png";

    private static CloudinaryPrintFileSource Source(bool aiUpscale = false) =>
        new(Microsoft.Extensions.Options.Options.Create(new PrintifyOptions { AiUpscale = aiUpscale }));

    [TestMethod]
    public void Resized_ASmallDesign_IsScaledToThePrintWidthOnDelivery()
    {
        Source().Resized(Design, 1500, 1800, 4250)
            .Should().Be("https://res.cloudinary.com/demo/image/upload/c_scale,w_4250/f_png/v17/apcs/design-images/a.png");
    }

    [TestMethod]
    public void Resized_WithAiUpscale_EnlargesWithAiFirst()
    {
        Source(aiUpscale: true).Resized(Design, 1500, 1800, 4250)
            .Should().Be("https://res.cloudinary.com/demo/image/upload/e_upscale/c_scale,w_4250/f_png/v17/apcs/design-images/a.png");
    }

    [TestMethod]
    public void Resized_WithAiUpscale_ADesignTooLargeForIt_IsOnlyScaled()
    {
        Source(aiUpscale: true).Resized(Design, 2500, 2500, 4500).Should().NotContain("e_upscale");
    }

    [TestMethod]
    public void Resized_IsKeptUnderTheLargestImageCloudinaryDelivers()
    {
        // A square design at 6000px wide would be 36 MP.
        Source().Resized(Design, 2000, 2000, 6000).Should().Contain($"w_{(int)Math.Floor(Math.Sqrt(CloudinaryPrintFileSource.MaxOutputPixels))}");
    }

    [TestMethod]
    public void Resized_ADesignAlreadyWideEnough_OrStoredElsewhere_GivesNoFile()
    {
        Source().Resized(Design, 4500, 5400, 4250).Should().BeNull();
        Source().Resized("https://cdn.test/design.png", 1500, 1800, 4250).Should().BeNull();
    }
}

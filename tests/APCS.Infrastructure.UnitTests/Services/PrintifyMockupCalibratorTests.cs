using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class PrintifyMockupCalibratorTests
{
    private static readonly Rgb24 Backdrop = new(210, 205, 200);
    private static readonly Rgb24 Shirt = new(245, 245, 245);
    private static readonly Rgb24 Magenta = new(250, 10, 245);

    private static PrintifyMockupCalibrator Calibrator() => new(new Mock<IHttpClientFactory>().Object);

    // A shirt-colored photo with a print area at (500, 400) 600 x 800; the sheet photo shows it magenta with a cross.
    private static byte[] Photo(int size = 1600, bool sheet = false, Rgb24? backdrop = null, Rgb24? fabric = null)
    {
        using var image = new Image<Rgb24>(size, size, backdrop ?? Backdrop);
        for (var y = 300; y < Math.Min(size, 1300); y++)
            for (var x = 400; x < Math.Min(size, 1200); x++)
                image[x, y] = fabric ?? Shirt;
        if (sheet)
        {
            for (var y = 400; y < 1200 && y < size; y++)
                for (var x = 500; x < 1100 && x < size; x++)
                    image[x, y] = Math.Abs(x - 800) < 5 || Math.Abs(y - 800) < 5 ? new Rgb24(0, 0, 0) : Magenta;
        }
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    [TestMethod]
    public void DetectPrintArea_FindsTheMagentaRectangle()
    {
        var result = Calibrator().DetectPrintArea(Photo(sheet: true), Photo());

        result.Found.Should().BeTrue(result.Problem);
        result.Area.Should().Be(new PrintAreaOnPhoto(500, 400, 600, 800));
        (result.Width, result.Height).Should().Be((1600, 1600));
    }

    [TestMethod]
    public void DetectPrintArea_PhotosOfDifferentSizes_AreRefused() =>
        Calibrator().DetectPrintArea(Photo(sheet: true), Photo(size: 1700)).Problem.Should().Contain("not the same size");

    [TestMethod]
    public void DetectPrintArea_SmallPhoto_IsRefused() =>
        Calibrator().DetectPrintArea(Photo(size: 1200, sheet: true), Photo(size: 1200)).Problem.Should().Contain("at least 1500");

    [TestMethod]
    public void DetectPrintArea_NoMagenta_IsRefused() =>
        Calibrator().DetectPrintArea(Photo(), Photo()).Problem.Should().Contain("does not show");

    [TestMethod]
    public void DetectPrintArea_BarePhotoStillMagenta_IsRefused() =>
        Calibrator().DetectPrintArea(Photo(sheet: true), Photo(sheet: true)).Problem.Should().Contain("still shows");

    [TestMethod]
    public void DetectPrintArea_PhotosFramedDifferently_AreRefused() =>
        Calibrator().DetectPrintArea(Photo(sheet: true), Photo(backdrop: new Rgb24(120, 140, 160))).Problem.Should().Contain("framed differently");

    [TestMethod]
    public void MeasureFabric_GivesTheColorThatRecolorsTheWhitePhotoToPrintifys()
    {
        var area = new PrintAreaOnPhoto(500, 400, 600, 800);

        var result = Calibrator().MeasureFabric(Photo(fabric: new Rgb24(100, 50, 25)), Photo(fabric: new Rgb24(200, 200, 200)), area);

        result.Should().Be(new FabricMeasurement("#643219", "#804020"));
    }

    [TestMethod]
    public void MockupUrl_OnlyPrintifyMockups_At2048()
    {
        PrintifyMockupCalibrator.MockupUrl("https://images.printify.com/mockup/abc/123/456/x.jpg?camera_label=front&s=400")!.ToString()
            .Should().Be("https://images.printify.com/mockup/abc/123/456/x.jpg?camera_label=front&s=2048");
        PrintifyMockupCalibrator.MockupUrl("https://images-api.printify.com/mockup/abc/123/456/x.jpg?camera_label=front")!.ToString()
            .Should().Be("https://images-api.printify.com/mockup/abc/123/456/x.jpg?camera_label=front&s=2048");
        PrintifyMockupCalibrator.MockupUrl("http://images.printify.com/mockup/abc/123/456/x.jpg").Should().BeNull();
        PrintifyMockupCalibrator.MockupUrl("https://images-api.printify.com.evil.example/mockup/x.jpg").Should().BeNull();
        PrintifyMockupCalibrator.MockupUrl("https://evil.example.com/mockup/abc/123/456/x.jpg").Should().BeNull();
        PrintifyMockupCalibrator.MockupUrl("https://images.printify.com/other/x.jpg").Should().BeNull();
        PrintifyMockupCalibrator.MockupUrl("https://images.printify.com:8443/mockup/x.jpg").Should().BeNull();
    }

    [TestMethod]
    public async Task DownloadMockupAsync_AnotherHost_IsNeverRequested()
    {
        var clients = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var act = () => new PrintifyMockupCalibrator(clients.Object).DownloadMockupAsync("https://169.254.169.254/latest", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        clients.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Sheets_AreTheEmbeddedCalibrationAndBlankFiles()
    {
        var (calibration, blank) = Calibrator().Sheets();

        Image.Identify(calibration).Size.Should().Be(new Size(1200, 3600));
        Image.Identify(blank).Size.Should().Be(new Size(1200, 1200));
    }
}

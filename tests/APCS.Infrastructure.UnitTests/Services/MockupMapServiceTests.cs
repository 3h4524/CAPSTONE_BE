using System.Net;
using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class MockupMapServiceTests
{
    private const string MaskUrl = "https://res.cloudinary.com/demo/image/upload/v1/mockup-templates/t-1-mask.png";
    private const string PhotoUrl = "https://res.cloudinary.com/demo/image/upload/v1/mockup-templates/t-1.avif";

    private readonly Mock<IMockupMapGenerator> _generator = new(MockBehavior.Strict);
    private readonly Mock<IPublicImageService> _images = new();
    private readonly FakeHandler _http = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private MockupMapService CreateService()
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient(MockupMapService.HttpClientName)).Returns(() => new HttpClient(_http, disposeHandler: false));
        return new MockupMapService(clients.Object, _generator.Object, _images.Object, _cache);
    }

    [TestCleanup]
    public void Cleanup() => _cache.Dispose();

    // A transparent picture with an opaque box for each garment, given as (left, top, right, bottom) in pixels.
    private static byte[] Mask(int width, int height, params (int Left, int Top, int Right, int Bottom)[] garments)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        foreach (var (left, top, right, bottom) in garments)
            for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                    image[x, y] = new Rgba32(255, 255, 255, 255);
        return Png(image);
    }

    private static byte[] Photo(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        return Png(image);
    }

    private static byte[] Png(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static GarmentMaskResult Garment(byte[] png) => new(png, 0.4, 0.8, 100, 120);

    [TestMethod]
    public void IsAvailable_IsTheGenerators()
    {
        _generator.SetupGet(x => x.IsAvailable).Returns(true);

        CreateService().IsAvailable.Should().BeTrue();
    }

    [TestMethod]
    public async Task PrepareAsync_FullSize_MakesTheMaskTheMapAndTheCutMask()
    {
        var photo = new byte[] { 1 };
        _generator.Setup(x => x.GenerateGarmentMask(photo, null, null)).Returns(Garment([2]));
        _generator.Setup(x => x.GenerateDisplacementMap(photo, null)).Returns([3]);
        _generator.Setup(x => x.GenerateCutMask(It.Is<byte[]>(m => m[0] == 2))).Returns([4]);

        var prepared = await CreateService().PrepareAsync(photo);

        prepared.MaskPng.Should().Equal(2);
        prepared.DisplacementPng.Should().Equal(3);
        prepared.CutMaskPng.Should().Equal(4);
        prepared.Stats.Should().Be(new GarmentMaskStats(0.4, 0.8));
        prepared.WidthPx.Should().Be(100);
    }

    [TestMethod]
    public async Task PrepareAsync_Preview_MakesOnlyASmallMask()
    {
        var photo = new byte[] { 1 };
        _generator.Setup(x => x.GenerateGarmentMask(photo, MockupMapService.PreviewMaxSide, null)).Returns(Garment([2]));

        var prepared = await CreateService().PrepareAsync(photo, preview: true);

        prepared.DisplacementPng.Should().BeNull();
        prepared.CutMaskPng.Should().BeNull();
    }

    [TestMethod]
    public async Task PrepareModelPhotoAsync_WithTheSellersSelection_UsesIt_AndMakesNoCutMask()
    {
        var photo = new byte[] { 1 };
        var selection = new byte[] { 9 };
        _generator.Setup(x => x.GenerateGarmentMask(photo, null, selection)).Returns(Garment([2]));
        _generator.Setup(x => x.GenerateDisplacementMap(photo, selection)).Returns([3]);

        var prepared = await CreateService().PrepareModelPhotoAsync(photo, selection);

        prepared!.MaskPng.Should().Equal(2);
        prepared.DisplacementPng.Should().Equal(3);
        prepared.CutMaskPng.Should().BeNull();
    }

    [TestMethod]
    public async Task PrepareModelPhotoAsync_NoGarmentFound_IsNull()
    {
        var photo = new byte[] { 1 };
        _generator.Setup(x => x.GenerateWornGarmentMask(photo, null)).Returns((byte[]?)null);

        (await CreateService().PrepareModelPhotoAsync(photo, null)).Should().BeNull();
    }

    [TestMethod]
    public async Task PreviewModelPhotoAsync_CountsTheGarmentsFound()
    {
        var photo = new byte[] { 1 };
        var worn = Mask(200, 120, (20, 20, 70, 110), (130, 20, 180, 110));
        _generator.Setup(x => x.GenerateGarmentMask(photo, MockupMapService.ModelPreviewMaxSide, null)).Returns(Garment([2]));
        _generator.Setup(x => x.GenerateWornGarmentMask(photo, MockupMapService.ModelPreviewMaxSide)).Returns(worn);

        var prepared = await CreateService().PreviewModelPhotoAsync(photo);

        prepared.WornGarmentPng.Should().Equal(worn);
        prepared.GarmentCount.Should().Be(2);
    }

    [TestMethod]
    public async Task PreviewModelPhotoAsync_NoGarmentFound_HasNoCount()
    {
        var photo = new byte[] { 1 };
        _generator.Setup(x => x.GenerateGarmentMask(photo, MockupMapService.ModelPreviewMaxSide, null)).Returns(Garment([2]));
        _generator.Setup(x => x.GenerateWornGarmentMask(photo, MockupMapService.ModelPreviewMaxSide)).Returns((byte[]?)null);

        (await CreateService().PreviewModelPhotoAsync(photo)).GarmentCount.Should().BeNull();
    }

    [TestMethod]
    public async Task MeasureGarmentAsync_FetchesASmallMaskOnce_AndKeepsWhatItMeasured()
    {
        _http.Respond(MockupMapService.Limited(MaskUrl, MockupMapService.MeasureMaxSide), Mask(100, 120, (30, 20, 70, 110)));
        var service = CreateService();

        var first = await service.MeasureGarmentAsync(MaskUrl);
        var second = await service.MeasureGarmentAsync(MaskUrl);

        first.Should().NotBeNull();
        first!.CenterX.Should().BeApproximately(0.5, 0.02);
        second.Should().Be(first);
        _http.Requests.Should().ContainSingle();
    }

    [TestMethod]
    public async Task MeasureGarmentsAsync_MeasuresEachGarmentLeftToRight_Once()
    {
        _http.Respond(MockupMapService.Limited(MaskUrl, MockupMapService.MeasureMaxSide), Mask(200, 120, (20, 20, 70, 110), (130, 20, 180, 110)));
        var service = CreateService();

        var first = await service.MeasureGarmentsAsync(MaskUrl);
        var second = await service.MeasureGarmentsAsync(MaskUrl);

        first.Should().HaveCount(2);
        first[0].CenterX.Should().BeLessThan(first[1].CenterX);
        second.Should().BeSameAs(first);
        _http.Requests.Should().ContainSingle();
    }

    [TestMethod]
    public async Task MeasureGarmentColorAsync_ReadsTheGarmentsColorOnce_EvenWhenTheSizesDifferByAPixel()
    {
        _http.Respond(MockupMapService.Limited(MockupMapService.AsJpeg(PhotoUrl), MockupMapService.ColorMaxSide), Photo(100, 120, new Rgba32(200, 30, 40)));
        _http.Respond(MockupMapService.Limited(MaskUrl, MockupMapService.ColorMaxSide), Mask(101, 120, (20, 20, 80, 110)));
        var service = CreateService();

        var first = await service.MeasureGarmentColorAsync(PhotoUrl, MaskUrl);
        var second = await service.MeasureGarmentColorAsync(PhotoUrl, MaskUrl);

        first.Should().StartWith("#");
        second.Should().Be(first);
        _http.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    public void Limited_AsksTheCdnForASmallerImage_OnlyForACdnUrl()
    {
        MockupMapService.Limited(MaskUrl, 400).Should().Contain("/upload/c_limit,w_400,h_400/v1/");
        MockupMapService.Limited("https://example.com/mask.png", 400).Should().Be("https://example.com/mask.png");
    }

    [TestMethod]
    public void AsJpeg_SwapsOrAddsTheExtension()
    {
        MockupMapService.AsJpeg(PhotoUrl).Should().EndWith("/t-1.jpg");
        MockupMapService.AsJpeg("https://res.cloudinary.com/demo/image/upload/v1/photo").Should().EndWith("/photo.jpg");
    }

    [TestMethod]
    public async Task DownloadAsync_FetchesTheJpegRendition()
    {
        _http.Respond(MockupMapService.AsJpeg(PhotoUrl), [7, 7]);

        (await CreateService().DownloadAsync(PhotoUrl)).Should().Equal(7, 7);
    }

    [TestMethod]
    public async Task ToJpegAsync_SavesTheImageAsAJpeg()
    {
        var jpeg = await CreateService().ToJpegAsync(Photo(20, 20, new Rgba32(10, 20, 30)));

        Image.DetectFormat(jpeg).Name.Should().Be("JPEG");
    }

    [TestMethod]
    public async Task StoreHelpersAsync_UploadsEachHelperImageItHas()
    {
        var stored = new List<string>();
        _images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<UploadFileDto, string, CancellationToken>((_, key, _) => stored.Add(key))
            .ReturnsAsync("https://example.com/stored.png");
        var service = CreateService();

        await service.StoreHelpersAsync("mockup-templates/t-1-3", new PreparedBasePhoto([1], [2], new GarmentMaskStats(0.4, 0.8), CutMaskPng: [3]));
        await service.StoreHelpersAsync("mockup-templates/t-2-1", new PreparedBasePhoto([1], null, new GarmentMaskStats(0.4, 0.8)));

        stored.Should().HaveCount(4);
        stored.Should().OnlyContain(key => key.StartsWith("mockup-templates/"));
    }

    [TestMethod]
    public async Task EnsureCutMaskAsync_AlreadyStored_MakesNothing()
    {
        _http.Respond("https://example.com/cut.png", [], HttpMethod.Head);

        (await CreateService().EnsureCutMaskAsync("https://example.com/mask.png", "https://example.com/cut.png", "key")).Should().BeTrue();
        _images.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task EnsureCutMaskAsync_Missing_MakesAndStoresIt()
    {
        _http.Respond("https://example.com/mask.png", [5]);
        _generator.Setup(x => x.GenerateCutMask(It.Is<byte[]>(m => m[0] == 5))).Returns([6]);
        _images.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), "cut-key", It.IsAny<CancellationToken>())).ReturnsAsync("https://example.com/cut.png");

        (await CreateService().EnsureCutMaskAsync("https://example.com/mask.png", "https://example.com/cut.png", "cut-key")).Should().BeTrue();
        _images.Verify(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), "cut-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EnsureCutMaskAsync_CannotBeMade_IsFalse()
    {
        _http.Respond("https://example.com/mask.png", [5]);
        _generator.Setup(x => x.GenerateCutMask(It.IsAny<byte[]>())).Returns((byte[]?)null);

        (await CreateService().EnsureCutMaskAsync("https://example.com/mask.png", "https://example.com/cut.png", "cut-key")).Should().BeFalse();
    }

    // Answers known URLs with their bytes and anything else with 404, and records what was asked.
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod, string), byte[]> _responses = [];
        public List<string> Requests { get; } = [];

        public void Respond(string url, byte[] body, HttpMethod? method = null) => _responses[(method ?? HttpMethod.Get, url)] = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(_responses.TryGetValue((request.Method, url), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

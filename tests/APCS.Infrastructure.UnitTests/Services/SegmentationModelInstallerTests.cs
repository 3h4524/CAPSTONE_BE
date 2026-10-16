using System.Net;
using System.Security.Cryptography;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class SegmentationModelInstallerTests
{
    private static readonly byte[] Model = [1, 2, 3, 4, 5];
    private readonly LoadingSegmenter _segmenter = new();
    private string _root = null!;
    private string _contentRoot = null!;

    [TestInitialize]
    public void CreateFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), $"apcs-installer-{Guid.NewGuid():N}");
        _contentRoot = Path.Combine(_root, "API");
        Directory.CreateDirectory(_contentRoot);
    }

    [TestCleanup]
    public void DeleteFolders() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task StartAsync_ModelMissing_DownloadsItWhereTheSegmenterLooks()
    {
        var handler = new FakeHandler(Model);

        await Installer(handler, Sha256(Model)).StartAsync(CancellationToken.None);

        var installed = BiRefNetGarmentSegmenter.ResolvePath("models/model.onnx", _contentRoot);
        installed.Should().NotBeNull();
        File.ReadAllBytes(installed!).Should().Equal(Model);
        Directory.GetFiles(Path.GetDirectoryName(installed)!).Should().ContainSingle("the temporary file is gone");
    }

    [TestMethod]
    public async Task StartAsync_ModelPresent_DoesNotDownload()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "models"));
        File.WriteAllBytes(Path.Combine(_contentRoot, "models", "model.onnx"), [9]);
        var handler = new FakeHandler(Model);

        await Installer(handler, Sha256(Model)).StartAsync(CancellationToken.None);

        handler.Requests.Should().Be(0);
    }

    [TestMethod]
    public async Task StartAsync_WrongChecksum_InstallsNothingAndStillStarts()
    {
        var handler = new FakeHandler(Model);

        await Installer(handler, Sha256([7, 7, 7])).StartAsync(CancellationToken.None);

        BiRefNetGarmentSegmenter.ResolvePath("models/model.onnx", _contentRoot).Should().BeNull();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_DownloadFails_StillStarts()
    {
        var handler = new FakeHandler(Model, HttpStatusCode.NotFound);

        var start = () => Installer(handler, Sha256(Model)).StartAsync(CancellationToken.None);

        await start.Should().NotThrowAsync();
        BiRefNetGarmentSegmenter.ResolvePath("models/model.onnx", _contentRoot).Should().BeNull();
    }

    [TestMethod]
    public async Task StartAsync_NoUrl_DoesNotDownload()
    {
        var handler = new FakeHandler(Model);

        await Installer(handler, Sha256(Model), url: "").StartAsync(CancellationToken.None);

        handler.Requests.Should().Be(0);
    }

    [TestMethod]
    public async Task StartAsync_LoadsTheModelInTheBackground()
    {
        await Installer(new FakeHandler(Model), Sha256(Model)).StartAsync(CancellationToken.None);

        var loaded = await Task.WhenAny(_segmenter.Loaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        loaded.Should().BeSameAs(_segmenter.Loaded.Task, "the first photo should not be the one that waits for the model");
    }

    [TestMethod]
    public void InstallPath_InACheckout_IsBesideTheSolution()
    {
        File.WriteAllText(Path.Combine(_root, "App.sln"), "");

        SegmentationModelInstaller.InstallPath("models/model.onnx", _contentRoot)
            .Should().Be(Path.GetFullPath(Path.Combine(_root, "models", "model.onnx")));
    }

    [TestMethod]
    public void InstallPath_Deployed_IsUnderTheContentRoot()
    {
        SegmentationModelInstaller.InstallPath("models/model.onnx", _contentRoot)
            .Should().Be(Path.GetFullPath(Path.Combine(_contentRoot, "models", "model.onnx")));
    }

    private SegmentationModelInstaller Installer(FakeHandler handler, string sha256, string url = "https://models.test/model.onnx")
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(f => f.CreateClient(SegmentationModelInstaller.HttpClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var options = Microsoft.Extensions.Options.Options.Create(new MockupOptions
        {
            SegmentationModelPath = "models/model.onnx",
            SegmentationModelUrl = url,
            SegmentationModelSha256 = sha256
        });
        return new SegmentationModelInstaller(clients.Object, options, new FakeEnvironment(_contentRoot), _segmenter, NullLogger<SegmentationModelInstaller>.Instance);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // Stands in for the model; reading IsAvailable is what loads the real one.
    private sealed class LoadingSegmenter : IGarmentSegmenter
    {
        public TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable
        {
            get
            {
                Loaded.TrySetResult();
                return true;
            }
        }

        public byte[]? Segment(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> photo, int width, int height, bool quick = false) => null;
    }

    private sealed class FakeHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class FakeEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

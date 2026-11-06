using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class MiniMaxFirstFramePreparerTests
{
    [TestMethod]
    [DataRow("9:16", 270, 480)]
    [DataRow("16:9", 640, 360)]
    public async Task PrepareAsync_CroppedSource_MatchesPixelsSentByMiniMax(string aspect, int expectedWidth, int expectedHeight)
    {
        using var image = new Image<Rgba32>(800, 800);
        for (var y = 0; y < 800; y++)
        for (var x = 0; x < 800; x++) image[x, y] = new((byte)(x % 256), (byte)(y % 256), 0);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var source = stream.ToArray();
        string? submitted = null;
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(source) };
            submitted = await request.Content!.ReadAsStringAsync();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"task_id\":\"video-test\"}", Encoding.UTF8, "application/json") };
        })));
        var crop = new Region(.1, .2, .8, .6);

        var analysisPixels = await new MiniMaxFirstFramePreparer(clients.Object).PrepareAsync("https://private.test/frame", aspect, crop, default);
        await new MiniMaxVideoProvider(clients.Object, NullLogger<MiniMaxVideoProvider>.Instance)
            .StartAsync("test-key", "https://private.test/frame", "One continuous shot.", aspect, 6, false, crop, default);

        using var body = JsonDocument.Parse(submitted!);
        var uri = body.RootElement.GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!;
        var providerPixels = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        providerPixels.Should().Equal(analysisPixels);
        using var prepared = Image.Load<Rgba32>(analysisPixels);
        prepared.Width.Should().Be(expectedWidth);
        prepared.Height.Should().Be(expectedHeight);
        prepared[0, 0].Should().Be(aspect == "9:16" ? image[265, 160] : image[80, 220]);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

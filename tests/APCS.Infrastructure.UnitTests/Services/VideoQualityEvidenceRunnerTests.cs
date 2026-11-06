using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class VideoQualityEvidenceRunnerTests
{
    [TestMethod]
    public async Task EvaluateBatchAsync_SendsContentLengthAndReturnsSceneResults()
    {
        var handler = new CheckingHandler();
        var factory = new ClientFactory(handler);
        var options = Microsoft.Extensions.Options.Options.Create(new VideoQualityOptions
        {
            Enabled = true, WorkerUrl = "http://localhost:8099", WorkerToken = "test-token"
        });
        var runner = new VideoQualityEvidenceRunner(options, factory, new ProcessVideoQualityEvidenceRunner(options, factory));

        var result = await runner.EvaluateBatchAsync([
            new VideoQualityEvidenceRequest("https://example.test/scene.mp4", [1, 2, 3], 2)
        ]);

        Assert.IsTrue(handler.Checked);
        Assert.AreEqual("completed", result[2].Status);
        Assert.AreEqual("test-v1", result[2].EvaluatorVersion);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CheckingHandler : HttpMessageHandler
    {
        public bool Checked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("test-token", request.Headers.GetValues("X-Worker-Token").Single());
            Assert.IsNotNull(request.Content);
            var declaredLength = request.Content.Headers.ContentLength;
            var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.AreEqual((long)body.Length, declaredLength);
            Assert.IsTrue(declaredLength > 0);
            using var document = JsonDocument.Parse(body);
            Assert.AreEqual(2, document.RootElement.GetProperty("scenes")[0].GetProperty("sceneOrder").GetInt32());
            Checked = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[{\"sceneOrder\":2,\"status\":\"completed\",\"evaluatorVersion\":\"test-v1\",\"metrics\":{}}]}", Encoding.UTF8, "application/json")
            };
        }
    }
}

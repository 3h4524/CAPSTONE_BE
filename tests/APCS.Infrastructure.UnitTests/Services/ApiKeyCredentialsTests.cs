using System.Net;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ApiKeyCredentialsTests
{
    [TestMethod]
    public void Protection_RoundTripsAndRejectsTampering()
    {
        var service = new ApiKeyCredentials(new EphemeralDataProtectionProvider(), Mock.Of<IHttpClientFactory>());
        var encrypted = service.Protect("synthetic-key-1234");
        encrypted.Should().NotContain("synthetic-key-1234");
        service.Unprotect(encrypted).Should().Be("synthetic-key-1234");
        Action tampered = () => service.Unprotect("corrupted-" + encrypted);
        tampered.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [TestMethod]
    [DataRow("openai", "https://api.openai.com/v1/models")]
    [DataRow("replicate", "https://api.replicate.com/v1/account")]
    [DataRow("printify", "https://api.printify.com/v1/shops.json")]
    public async Task Validation_UsesFixedEndpointAndBearerWithoutRequestBody(string provider, string endpoint)
    {
        using var handler = new Handler(request =>
        {
            request.RequestUri!.AbsoluteUri.Should().Be(endpoint);
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("synthetic-key");
            request.Content.Should().BeNull();
            request.Method.Should().Be(HttpMethod.Get);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("ApiKeyValidation")).Returns(client);
        var service = new ApiKeyCredentials(new EphemeralDataProtectionProvider(), factory.Object);
        (await service.ValidateAsync(provider, "synthetic-key", CancellationToken.None)).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(401)] [DataRow(403)] [DataRow(429)] [DataRow(500)] [DataRow(302)]
    public async Task Validation_RejectsNonSuccessResponses(int status)
    {
        using var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)status));
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("ApiKeyValidation")).Returns(client);
        var service = new ApiKeyCredentials(new EphemeralDataProtectionProvider(), factory.Object);
        (await service.ValidateAsync("openai", "synthetic-key", CancellationToken.None)).Should().BeFalse();
    }

    [TestMethod]
    public async Task Validation_UnknownProviderDoesNotSendCredential()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var service = new ApiKeyCredentials(new EphemeralDataProtectionProvider(), factory.Object);
        (await service.ValidateAsync("https://untrusted.example", "synthetic-key", CancellationToken.None)).Should().BeFalse();
        factory.VerifyNoOtherCalls();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}

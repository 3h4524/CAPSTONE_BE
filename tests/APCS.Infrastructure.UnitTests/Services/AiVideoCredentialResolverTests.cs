using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class AiVideoCredentialResolverTests
{
    private readonly Mock<IApiKeyRepository> keys = new();
    private readonly Mock<IApiKeyCredentials> credentials = new();

    [TestMethod]
    public async Task ResolveAsync_MiniMaxRoute_ReturnsTrimmedSystemKeyWithoutReadingSellerKeys()
    {
        var resolver = Create("  system-minimax-key  ");

        var result = await resolver.ResolveAsync(Guid.NewGuid(), AiVideoModel.ProviderKey, default);

        result.Should().Be("system-minimax-key");
        keys.VerifyNoOtherCalls();
        credentials.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResolveAsync_MiniMaxRouteWithoutSystemKey_DoesNotFallBackToSellerKey()
    {
        var owner = Guid.NewGuid();
        keys.Setup(x => x.ListOwnedAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = AiVideoModel.ProviderKey, IsActive = true, LastCheckSucceeded = true, KeyValueEncrypted = "encrypted" }]);
        var resolver = Create(string.Empty);

        var result = await resolver.ResolveAsync(owner, AiVideoModel.ProviderKey, default);

        result.Should().BeNull();
        keys.VerifyNoOtherCalls();
        credentials.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ResolveAsync_LegacyVeoRoute_ReturnsConnectedSellerGeminiKey()
    {
        var owner = Guid.NewGuid();
        keys.Setup(x => x.ListOwnedAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = VeoVideoModel.ProviderKey, IsActive = true, LastCheckSucceeded = true, KeyValueEncrypted = "encrypted" }]);
        credentials.Setup(x => x.Unprotect("encrypted")).Returns("seller-gemini-key");
        var resolver = Create("system-minimax-key");

        var result = await resolver.ResolveAsync(owner, VeoVideoModel.ProviderKey, default);

        result.Should().Be("seller-gemini-key");
    }

    private AiVideoCredentialResolver Create(string systemKey) =>
        new(Microsoft.Extensions.Options.Options.Create(new MiniMaxOptions { ApiKey = systemKey }), keys.Object, credentials.Object);
}

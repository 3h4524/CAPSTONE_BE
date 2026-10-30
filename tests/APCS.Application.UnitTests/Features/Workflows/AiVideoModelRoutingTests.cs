using APCS.Application.Abstractions.AI;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiVideoModelRoutingTests
{
    [TestMethod]
    public void LegacyAiShotJson_WithoutRoutingFields_DefaultsToVeoIdentity()
    {
        var json = $$"""
        {
          "sceneOrder": 0,
          "mockupId": "{{Guid.NewGuid()}}",
          "sourceHash": "{{new string('a', 64)}}",
          "model": "{{VeoVideoModel.ModelName}}",
          "preset": "lifestyle_in_use",
          "prompt": "A person naturally uses the product.",
          "durationSeconds": 6,
          "fingerprint": "{{new string('b', 64)}}"
        }
        """;

        var shot = WorkflowJson.Read<AiShotPlan>(json);

        shot.ProviderKey.Should().Be(VeoVideoModel.ProviderKey);
        shot.ModelKey.Should().Be(VeoVideoModel.ModelKey);
        shot.Resolution.Should().Be(VeoVideoModel.Resolution);
        shot.PricePerSecondUsd.Should().Be(VeoVideoModel.PricePerSecondUsd);
    }

    [TestMethod]
    public void Factory_ResolvesCapturedMiniMaxAndLegacyVeoRoutes()
    {
        var minimax = Strategy(AiVideoModel.ProviderKey, AiVideoModel.ModelKey, AiVideoModel.ModelName);
        var veo = Strategy(VeoVideoModel.ProviderKey, VeoVideoModel.ModelKey, VeoVideoModel.ModelName);
        var factory = new AiVideoGenerationStrategyFactory([minimax.Object, veo.Object]);

        factory.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey).Should().BeSameAs(minimax.Object);
        factory.Resolve(VeoVideoModel.ProviderKey, VeoVideoModel.ModelKey).Should().BeSameAs(veo.Object);
    }

    private static Mock<IAiVideoGenerationStrategy> Strategy(string provider, string key, string model)
    {
        var strategy = new Mock<IAiVideoGenerationStrategy>();
        strategy.SetupGet(x => x.ProviderKey).Returns(provider);
        strategy.SetupGet(x => x.ModelKey).Returns(key);
        strategy.SetupGet(x => x.ModelName).Returns(model);
        return strategy;
    }
}

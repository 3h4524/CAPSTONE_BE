using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class AssistantRegistrationTests
{
    [TestMethod]
    public void AddInfrastructure_RegistersGeminiAssistantChatModel()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IAssistantChatModel>().Should().BeOfType<GeminiAssistantChatModel>();
    }

    [TestMethod]
    public void AssistantOptions_WhenOwnKeyIsSet_UsesIt()
    {
        using var provider = BuildProvider(new()
        {
            ["Assistant:ApiKey"] = "own-key",
            ["Gemini:TextGeneration:ApiKey"] = "text-key",
            ["Gemini:Embeddings:ApiKey"] = "embedding-key"
        });

        provider.GetRequiredService<IOptions<AssistantOptions>>().Value.ApiKey.Should().Be("own-key");
    }

    [TestMethod]
    public void AssistantOptions_WhenOwnKeyMissing_FallsBackToTextGenerationKey()
    {
        using var provider = BuildProvider(new()
        {
            ["Gemini:TextGeneration:ApiKey"] = "text-key",
            ["Gemini:Embeddings:ApiKey"] = "embedding-key"
        });

        provider.GetRequiredService<IOptions<AssistantOptions>>().Value.ApiKey.Should().Be("text-key");
    }

    [TestMethod]
    public void AssistantOptions_WhenOnlyEmbeddingKeyIsSet_FallsBackToEmbeddingKey()
    {
        using var provider = BuildProvider(new()
        {
            ["Gemini:TextGeneration:ApiKey"] = "   ",
            ["Gemini:Embeddings:ApiKey"] = "embedding-key"
        });

        provider.GetRequiredService<IOptions<AssistantOptions>>().Value.ApiKey.Should().Be("embedding-key");
    }

    [TestMethod]
    public void AssistantOptions_WhenNoKeyIsSet_IsNotConfigured()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IOptions<AssistantOptions>>().Value.IsConfigured.Should().BeFalse();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=apcs_tests;Username=postgres;Password=postgres",
            ["Jwt:SigningKey"] = new string('k', 32),
            ["Redis:ConnectionString"] = "localhost:6379",
            ["App:BaseUrl"] = "http://localhost:3000"
        };
        foreach (var (key, value) in overrides)
            values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}

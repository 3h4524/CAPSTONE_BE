using APCS.Application.Abstractions.AI;
using APCS.Application.Features.DesignGeneration.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ImageGenerationProviderRegistrationTests
{
    [TestMethod]
    public void AddInfrastructure_RegistersEveryImageProvider()
    {
        using var services = BuildServices();
        using var scope = services.CreateScope();

        // Each service picks from all of them by name: design generation by the job's choice, the rest Gemini.
        scope.ServiceProvider.GetServices<IImageGenerationProvider>().Select(x => x.Provider)
            .Should().BeEquivalentTo(ImageGenerationProviders.All);
    }

    [TestMethod]
    public void AddInfrastructure_DesignGenerationSettings_TakeEachProvidersOwnLimit()
    {
        using var services = BuildServices(new()
        {
            ["Gemini:MaxConcurrentImages"] = "3",
            ["Ideogram:MaxConcurrentImages"] = "7"
        });

        var settings = services.GetRequiredService<IOptions<DesignGenerationSettings>>().Value;

        settings.MaxConcurrentImagesFor(ImageGenerationProviders.Gemini).Should().Be(3);
        settings.MaxConcurrentImagesFor(ImageGenerationProviders.Ideogram).Should().Be(7);
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=apcs_tests;Username=postgres;Password=postgres",
            ["Jwt:SigningKey"] = new string('k', 32),
            ["Redis:ConnectionString"] = "localhost:6379",
            ["App:BaseUrl"] = "http://localhost:3000"
        };
        foreach (var (key, value) in extra ?? []) values[key] = value;

        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }
}

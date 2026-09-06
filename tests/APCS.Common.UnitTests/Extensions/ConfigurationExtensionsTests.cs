using APCS.Common.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace APCS.Common.UnitTests.Extensions;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationExtensionsTests
{
    private const string EnvironmentKey = "APCS__UNIT_TEST__VALUE";

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(EnvironmentKey, null);
        Environment.SetEnvironmentVariable("ConnectionStrings__UnitTest", null);
    }

    [TestMethod]
    public void GetRequiredValue_WhenConfigurationContainsValue_PrefersConfiguration()
    {
        Environment.SetEnvironmentVariable(EnvironmentKey, "environment");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["APCS:UNIT_TEST:VALUE"] = "configuration"
        });

        configuration.GetRequiredValue("APCS:UNIT_TEST:VALUE").Should().Be("configuration");
    }

    [TestMethod]
    public void GetRequiredValue_WhenOnlyEnvironmentContainsValue_UsesEnvironmentKey()
    {
        Environment.SetEnvironmentVariable(EnvironmentKey, "environment");
        var configuration = BuildConfiguration([]);

        configuration.GetRequiredValue("APCS:UNIT_TEST:VALUE").Should().Be("environment");
    }

    [TestMethod]
    public void GetRequiredValue_WhenMissing_ThrowsDescriptiveException()
    {
        var configuration = BuildConfiguration([]);

        var act = () => configuration.GetRequiredValue("APCS:UNIT_TEST:VALUE");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*APCS:UNIT_TEST:VALUE*");
    }

    [TestMethod]
    public void GetOptionalStringArray_FromEnvironment_SplitsAndTrimsValues()
    {
        Environment.SetEnvironmentVariable(EnvironmentKey, "one; two ;;three");
        var configuration = BuildConfiguration([]);

        configuration.GetOptionalStringArray("APCS:UNIT_TEST:VALUE")
            .Should().Equal("one", "two", "three");
    }

    [TestMethod]
    public void GetRequiredConnectionStringValue_FromEnvironment_ReturnsValue()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__UnitTest", "Host=test");
        var configuration = BuildConfiguration([]);

        configuration.GetRequiredConnectionStringValue("UnitTest").Should().Be("Host=test");
    }

    [TestMethod]
    public void GetRequiredOptions_WhenSectionExists_BindsOptions()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Sample:Name"] = "value",
            ["Sample:Count"] = "3"
        });

        var options = configuration.GetRequiredOptions<SampleOptions>("Sample");

        options.Name.Should().Be("value");
        options.Count.Should().Be(3);
    }

    [TestMethod]
    public void ToEnvironmentKey_WithSectionKey_ReplacesSeparators()
    {
        APCS.Common.Extensions.ConfigurationExtensions.ToEnvironmentKey("Section:Child")
            .Should().Be("Section__Child");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class SampleOptions
    {
        public string Name { get; init; } = string.Empty;

        public int Count { get; init; }
    }
}

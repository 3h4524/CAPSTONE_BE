using APCS.Application.Abstractions.AI;
using APCS.Application.UnitTests.TestSupport;
using FluentAssertions;

namespace APCS.Application.UnitTests.Abstractions.AI;

[TestClass]
public sealed class SystemApiKeyPoolExtensionsTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [TestMethod]
    public async Task GenerateImageAsync_KeyTheProviderRefuses_IsPassedOverForTheDay_AndTheImageAskedOfTheNext()
    {
        var pool = new FakeSystemApiKeyPool("ideogram", ("broke", 5), ("paid", 5));
        var asked = new List<string>();

        var result = await pool.GenerateImageAsync("ideogram", ownKey: null, key =>
        {
            asked.Add(key);
            return Task.FromResult(key == "broke"
                ? ImageGenerationResult.AccountRefused("Ideogram returned 402: no credits", "ideogram-4")
                : ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        });

        result.IsSuccess.Should().BeTrue();
        asked.Should().Equal("broke", "paid");
        pool.Disabled.Should().ContainSingle().Which.Key.Should().Be("broke");
    }

    [TestMethod]
    public async Task GenerateImageAsync_PlatformKeysUsedUp_GoesOnWithTheSellersOwnKey()
    {
        var pool = new FakeSystemApiKeyPool("ideogram", ("platform", 0));
        string? used = null;

        var result = await pool.GenerateImageAsync("ideogram", "own", key =>
        {
            used = key;
            return Task.FromResult(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        });

        result.IsSuccess.Should().BeTrue();
        used.Should().Be("own");
    }

    [TestMethod]
    public async Task GenerateImageAsync_PlatformKeysUsedUp_WithoutAKeyOfTheirOwn_SaysSoAndAsksNothing()
    {
        var pool = new FakeSystemApiKeyPool("ideogram", ("platform", 0));
        var asked = false;

        var result = await pool.GenerateImageAsync("ideogram", ownKey: null, _ =>
        {
            asked = true;
            return Task.FromResult(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        });

        asked.Should().BeFalse();
        result.StopsJob.Should().BeTrue();
        result.ErrorMessage.Should().Be(SystemApiKeyPoolExtensions.UsedUpMessage("ideogram"));
    }
}

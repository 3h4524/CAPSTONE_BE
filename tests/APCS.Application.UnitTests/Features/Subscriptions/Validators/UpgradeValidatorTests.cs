using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Subscriptions.Validators;

[TestClass]
public sealed class UpgradeValidatorTests
{
    private readonly UpgradeValidator sut = new();

    [TestMethod]
    public async Task Validate_WithAPlanSelected_Passes()
    {
        var result = await sut.ValidateAsync(new UpgradeRequestDto(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task Validate_WhenPlanIdIsEmpty_Fails()
    {
        var result = await sut.ValidateAsync(new UpgradeRequestDto(Guid.Empty));

        result.IsValid.Should().BeFalse();
    }
}

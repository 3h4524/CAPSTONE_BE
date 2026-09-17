using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Subscriptions.Validators;

[TestClass]
public sealed class CheckoutValidatorTests
{
    private readonly CheckoutValidator sut = new();

    [TestMethod]
    public async Task Validate_WithAValidMonthlyRequest_Passes()
    {
        var result = await sut.ValidateAsync(new CheckoutRequestDto(Guid.NewGuid(), "monthly"));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task Validate_WithAValidAnnualRequest_Passes()
    {
        var result = await sut.ValidateAsync(new CheckoutRequestDto(Guid.NewGuid(), "Annual"));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public async Task Validate_WhenPlanIdIsEmpty_Fails()
    {
        var result = await sut.ValidateAsync(new CheckoutRequestDto(Guid.Empty, "monthly"));

        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task Validate_WhenBillingCycleIsNotMonthlyOrAnnual_Fails()
    {
        var result = await sut.ValidateAsync(new CheckoutRequestDto(Guid.NewGuid(), "weekly"));

        result.IsValid.Should().BeFalse();
    }
}

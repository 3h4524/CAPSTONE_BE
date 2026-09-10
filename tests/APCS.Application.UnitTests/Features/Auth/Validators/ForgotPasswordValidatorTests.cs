using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class ForgotPasswordValidatorTests
{
    [TestMethod]
    public void ForgotPasswordValidator_WithValidRequest_IsValid()
    {
        new ForgotPasswordValidator()
            .Validate(new ForgotPasswordRequestDto("user@example.com"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Email")]
    [DataRow("invalid", "Email")]
    public void ForgotPasswordValidator_WithInvalidRequest_ContainsExpectedProperty(
        string email,
        string propertyName)
    {
        var result = new ForgotPasswordValidator().Validate(new ForgotPasswordRequestDto(email));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    public void ForgotPasswordValidator_WhenEmailExceedsMaximumLength_IsInvalid()
    {
        var request = new ForgotPasswordRequestDto($"{new string('a', 245)}@example.com");

        var result = new ForgotPasswordValidator().Validate(request);

        result.Errors.Should().Contain(error => error.PropertyName == "Email");
    }
}

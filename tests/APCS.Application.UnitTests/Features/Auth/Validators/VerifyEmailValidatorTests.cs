using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class VerifyEmailValidatorTests
{
    [TestMethod]
    public void VerifyEmailValidator_WithValidRequest_IsValid()
    {
        new VerifyEmailValidator()
            .Validate(new VerifyEmailRequestDto("raw-verification-token"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void VerifyEmailValidator_WithMissingToken_IsInvalid(string token)
    {
        var result = new VerifyEmailValidator().Validate(new VerifyEmailRequestDto(token));

        result.Errors.Should().Contain(error => error.PropertyName == "Token");
    }

    [TestMethod]
    public void VerifyEmailValidator_WhenTokenExceedsMaximumLength_IsInvalid()
    {
        var result = new VerifyEmailValidator().Validate(new VerifyEmailRequestDto(new string('a', 257)));

        result.Errors.Should().Contain(error => error.PropertyName == "Token");
    }
}

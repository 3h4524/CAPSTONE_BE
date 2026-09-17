using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class ResetPasswordValidatorTests
{
    private const string ValidToken = "raw-reset-token";

    [TestMethod]
    public void ResetPasswordValidator_WithValidRequest_IsValid()
    {
        new ResetPasswordValidator()
            .Validate(new ResetPasswordRequestDto(ValidToken, "Password1"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Password1", "Token")]
    [DataRow(ValidToken, "short", "NewPassword")]
    public void ResetPasswordValidator_WithInvalidRequest_ContainsExpectedProperty(
        string token,
        string newPassword,
        string propertyName)
    {
        var result = new ResetPasswordValidator().Validate(new ResetPasswordRequestDto(token, newPassword));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    [DataRow("password1", "uppercase")]
    [DataRow("PASSWORD1", "lowercase")]
    [DataRow("Password", "digit")]
    public void ResetPasswordValidator_WhenPasswordMissesRequiredCharacterClass_IsInvalid(
        string newPassword,
        string expectedMessageFragment)
    {
        var result = new ResetPasswordValidator().Validate(new ResetPasswordRequestDto(ValidToken, newPassword));

        result.Errors.Should().Contain(error =>
            error.PropertyName == "NewPassword"
            && error.ErrorMessage.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ResetPasswordValidator_WhenValuesExceedMaximums_IsInvalid()
    {
        var request = new ResetPasswordRequestDto(new string('a', 257), new string('P', 129));

        var result = new ResetPasswordValidator().Validate(request);

        result.Errors.Select(error => error.PropertyName)
            .Should().Contain(["Token", "NewPassword"]);
    }
}

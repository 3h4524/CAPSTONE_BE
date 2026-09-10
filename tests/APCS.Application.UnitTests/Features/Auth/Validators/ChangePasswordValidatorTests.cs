using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class ChangePasswordValidatorTests
{
    [TestMethod]
    public void ChangePasswordValidator_WithValidRequest_IsValid()
    {
        new ChangePasswordValidator()
            .Validate(new ChangePasswordRequestDto("CurrentPass1", "NewPassword1"))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "NewPassword1", "CurrentPassword")]
    [DataRow("CurrentPass1", "short", "NewPassword")]
    public void ChangePasswordValidator_WithInvalidRequest_ContainsExpectedProperty(
        string currentPassword,
        string newPassword,
        string propertyName)
    {
        var result = new ChangePasswordValidator().Validate(
            new ChangePasswordRequestDto(currentPassword, newPassword));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    [DataRow("password1", "uppercase")]
    [DataRow("PASSWORD1", "lowercase")]
    [DataRow("Password", "digit")]
    public void ChangePasswordValidator_WhenPasswordMissesRequiredCharacterClass_IsInvalid(
        string newPassword,
        string expectedMessageFragment)
    {
        var result = new ChangePasswordValidator().Validate(
            new ChangePasswordRequestDto("CurrentPass1", newPassword));

        result.Errors.Should().Contain(error =>
            error.PropertyName == "NewPassword"
            && error.ErrorMessage.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ChangePasswordValidator_WhenNewPasswordExceedsMaximumLength_IsInvalid()
    {
        var request = new ChangePasswordRequestDto("CurrentPass1", new string('P', 129));

        var result = new ChangePasswordValidator().Validate(request);

        result.Errors.Should().Contain(error => error.PropertyName == "NewPassword");
    }
}

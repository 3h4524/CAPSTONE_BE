using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Validators;

[TestClass]
public sealed class RegisterValidatorTests
{
    [TestMethod]
    public void RegisterValidator_WithValidRequest_IsValid()
    {
        var result = new RegisterValidator().Validate(
            new RegisterRequestDto("user@example.com", "Password1", "User Name"));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void RegisterValidator_WithoutFullName_IsValid(string? fullName)
    {
        // Full name is optional so clients that never collected one keep working; the service
        // falls back to the email local part.
        var result = new RegisterValidator().Validate(
            new RegisterRequestDto("user@example.com", "Password1", fullName));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Password1", "User", "Email")]
    [DataRow("invalid", "Password1", "User", "Email")]
    [DataRow("user@example.com", "short", "User", "Password")]
    public void RegisterValidator_WithInvalidRequest_ContainsExpectedProperty(
        string email,
        string password,
        string fullName,
        string propertyName)
    {
        var result = new RegisterValidator().Validate(new RegisterRequestDto(email, password, fullName));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    [DataRow("password1", "uppercase")]
    [DataRow("PASSWORD1", "lowercase")]
    [DataRow("Password", "digit")]
    public void RegisterValidator_WhenPasswordMissesRequiredCharacterClass_IsInvalid(
        string password,
        string expectedMessageFragment)
    {
        var result = new RegisterValidator().Validate(
            new RegisterRequestDto("user@example.com", password, "User"));

        result.Errors.Should().Contain(error =>
            error.PropertyName == "Password"
            && error.ErrorMessage.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RegisterValidator_WhenValuesExceedMaximums_IsInvalid()
    {
        var request = new RegisterRequestDto(
            $"{new string('a', 245)}@example.com",
            new string('P', 129),
            new string('N', 256));

        var result = new RegisterValidator().Validate(request);

        result.Errors.Select(error => error.PropertyName)
            .Should().Contain(["Email", "Password", "FullName"]);
    }
}

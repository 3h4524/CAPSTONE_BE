using APCS.Application.Features.Auth.Commands.Register;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Register;

[TestClass]
public sealed class RegisterValidatorTests
{
    [TestMethod]
    public void RegisterValidator_WithValidCommand_IsValid()
    {
        var result = new RegisterValidator().Validate(
            new RegisterCommand("user@example.com", "Password1", "User Name"));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void RegisterValidator_WithoutFullName_IsValid(string? fullName)
    {
        // Full name is optional so clients that never collected one keep working; the handler
        // falls back to the email local part.
        var result = new RegisterValidator().Validate(
            new RegisterCommand("user@example.com", "Password1", fullName));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("", "Password1", "User", "Email")]
    [DataRow("invalid", "Password1", "User", "Email")]
    [DataRow("user@example.com", "short", "User", "Password")]
    public void RegisterValidator_WithInvalidCommand_ContainsExpectedProperty(
        string email,
        string password,
        string fullName,
        string propertyName)
    {
        var result = new RegisterValidator().Validate(new RegisterCommand(email, password, fullName));

        result.Errors.Should().Contain(error => error.PropertyName == propertyName);
    }

    [TestMethod]
    public void RegisterValidator_WhenValuesExceedMaximums_IsInvalid()
    {
        var command = new RegisterCommand(
            $"{new string('a', 245)}@example.com",
            new string('P', 129),
            new string('N', 256));

        var result = new RegisterValidator().Validate(command);

        result.Errors.Select(error => error.PropertyName)
            .Should().Contain(["Email", "Password", "FullName"]);
    }
}
